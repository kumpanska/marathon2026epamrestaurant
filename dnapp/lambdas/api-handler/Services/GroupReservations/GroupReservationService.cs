using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.Locations;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupReservationService(
    IGroupReservationRepository groupReservationRepository,
    ITableRepository tableRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IUserService userService,
    ILocationService locationService,
    IOrderRepository? orderRepository = null) : IGroupReservationService
{
    private const string DefaultRestaurantName = "Green & Tasty";

    public async Task<GetGroupReservationResponse> GetGroupReservationByTokenAsync(string token)
    {
        GroupReservation groupReservation = await groupReservationRepository.GetGroupReservationByTokenAsync(token)
            ?? throw new GroupReservationNotFoundException();

        if (groupReservation.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException();

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(groupReservation.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(groupReservation.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new ValidationException("Associated reservation not found.");
        int joinedGuestsCount = await countTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException();

        Table table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new InvalidOperationException("Associated table not found.");

        Location? location = await locationService.GetLocationByIdAsync(table.LocationId);
        string restaurantName = DefaultRestaurantName;
        string address = location?.Address ?? "Unknown Address";

        string inviterName = "Unknown";
        if (!string.IsNullOrWhiteSpace(reservation.CustomerId))
        {
            inviterName = await userService.GetUserName(reservation.CustomerId) ?? "Unknown";
        }
        return new GetGroupReservationResponse(
            groupReservation.GroupReservationId,
            table.LocationId,
            restaurantName,
            address,
            reservation.StartTime,
            reservation.EndTime,
            reservation.GuestCount,
            table.TableNumber,
            inviterName);
    }

    public async Task<GroupReservation> GetGroupReservationAsync(string groupReservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        return groupReservation ?? throw new InvalidOperationException($"Group reservation with ID {groupReservationId} not found.");
    }

    public async Task<GroupReservation> CreateGroupReservationAsync(string reservationId)
    {
        var groupReservation = new GroupReservation
        {
            GroupReservationId = Guid.NewGuid().ToString(),
            ReservationId = reservationId,
            ReservationToken = Guid.NewGuid().ToString("N"),
            Status = GroupReservationStatus.Open,
        };

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);

        Reservation? reservation = await reservationRepository.GetReservationByIdAsync(reservationId);
        string? ownerUserId = reservation?.CustomerId;
        if (!string.IsNullOrWhiteSpace(ownerUserId))
        {
            await EnsureOwnerGuestSessionExistsAsync(
                ownerUserId,
                groupReservation.GroupReservationId);
        }

        return groupReservation;
    }

    public async Task CreateGroupReservationIfDoesNotExistAsync(string userId, string reservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (group is not null)
        {
            await EnsureOwnerGuestSessionExistsAsync(userId, group.GroupReservationId);
            return;
        }

        await CreateGroupReservationAsync(reservationId);
    }

    public async Task UpdateGroupReservationStatusAsync(string groupReservationId, GroupReservationStatus status)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        groupReservation.Status = status;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task DeleteGroupReservationAsync(string groupReservationId)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        await groupReservationRepository.DeleteGroupReservationAsync(groupReservation);
    }

    public async Task CloseGroupReservationAsync(string reservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (groupReservation is null)
            return;

        groupReservation.Status = GroupReservationStatus.Closed;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task<GetGroupInviteTokenResponse> GetGroupReservationInviteTokenAsync(string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (reservation.GuestCount < 2)
            throw new ValidationException("Retrieving invitation token for the reservation with only one guest is not permitted.");

        if (reservation.Status != ReservationStatus.Reserved)
            throw new GroupReservationExpiredException("The group reservation is expired.");

        GroupReservation groupReservation = (await groupReservationRepository.GetByReservationIdAsync(reservationId))!;

        if (groupReservation.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (groupReservation.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (groupReservation.Status == GroupReservationStatus.Submitting)
            throw new GroupReservationExpiredException();

        return new GetGroupInviteTokenResponse(groupReservation.ReservationToken);
    }

    public async Task<string> GetOwnerGuestSessionIdAsync(string userId, string reservationId)
    {
        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");
        EnsureReservationOwner(userId, reservation);

        GroupReservation groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();

        await EnsureOwnerGuestSessionExistsAsync(userId, groupReservation.GroupReservationId);

        groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(
            groupReservation.GroupReservationId) ?? throw new GroupReservationNotFoundException();
        GuestSession? ownerSession = string.IsNullOrWhiteSpace(groupReservation.OwnerGuestSessionId)
            ? null
            : await guestSessionRepository.GetByGuestSessionId(groupReservation.OwnerGuestSessionId);

        if (ownerSession is null ||
            !ownerSession.IsOwnerSession ||
            !string.Equals(ownerSession.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ownerSession.GroupReservationId, groupReservation.GroupReservationId, StringComparison.Ordinal))
        {
            throw new GuestSessionNotFoundException("Owner guest session was not found.");
        }

        return ownerSession.Id;
    }

    public async Task<List<ConsolidatedDishResponse>> SubmitGroupReservationAsync(
        string userId,
        string groupReservationId,
        string reservationId)
    {
        GroupReservation groupReservation =
            await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (!string.Equals(groupReservation.ReservationId, reservationId, StringComparison.Ordinal))
            throw new ValidationException("Reservation ID does not match the group reservation.");

        EnsureReservationOwner(userId, reservation);

        if (groupReservation.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (groupReservation.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        bool submissionClaimed = groupReservation.Status == GroupReservationStatus.Submitting;
        for (int attempt = 0; !submissionClaimed && attempt < 3; attempt++)
        {
            if (groupReservation.Status == GroupReservationStatus.Open &&
                await groupReservationRepository.TryBeginSubmissionAsync(groupReservationId))
            {
                submissionClaimed = true;
                break;
            }

            groupReservation =
                await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
                ?? throw new GroupReservationNotFoundException();

            if (groupReservation.Status == GroupReservationStatus.Submitted)
                throw new GroupReservationSubmittedException();

            if (groupReservation.Status == GroupReservationStatus.Closed)
                throw new GroupReservationExpiredException();

            submissionClaimed = groupReservation.Status == GroupReservationStatus.Submitting;
        }

        if (!submissionClaimed)
            throw new GroupReservationExpiredException();

        // A GSI query is eventually consistent. Once the group is closed to guest
        // submissions, use a strongly consistent base-table scan for the snapshot.
        List<GuestSession> sessions =
            await guestSessionRepository.GetAllByGroupReservationIdStronglyConsistentAsync(groupReservationId);
        List<GuestSession> submittedSessions = sessions.Where(session => session.IsSubmitted).ToList();
        if (submittedSessions.Count == 0)
        {
            await RestoreOpenBeforeValidationAsync(groupReservationId);
            throw new ValidationException("No submitted guest sessions were found.");
        }

        List<ConsolidatedDishResponse> consolidatedDishes = submittedSessions
            .SelectMany(session => session.Dishes ?? [])
            .GroupBy(dish => dish.DishId)
            .Select(group => new ConsolidatedDishResponse(group.Key, group.Sum(dish => dish.Quantity)))
            .ToList();

        if (consolidatedDishes.Count == 0)
        {
            await RestoreOpenBeforeValidationAsync(groupReservationId);
            throw new ValidationException("Submitted guest sessions contain no dishes.");
        }

        IOrderRepository configuredOrderRepository = orderRepository
            ?? throw new InvalidOperationException("Order repository is not configured.");
        Order? order;
        try
        {
            order = await configuredOrderRepository.GetOrderByReservationIdAsync(reservationId);
        }
        catch
        {
            await groupReservationRepository.TryRestoreOpenAfterFailedSubmissionAsync(groupReservationId);
            throw;
        }

        if (order is null)
        {
            await groupReservationRepository.TryRestoreOpenAfterFailedSubmissionAsync(groupReservationId);
            throw new ValidationException("Order not found.");
        }

        const int maxOrderConcurrencyRetries = 3;
        for (int attempt = 0; ; attempt++)
        {
            order.Dishes = MergeDishes(order.Dishes, consolidatedDishes);
            order.Status = OrderStatus.SubmittedByCustomer;

            try
            {
                bool submitted = await configuredOrderRepository.TrySaveOrderAndSubmitGroupReservationAsync(
                    order,
                    groupReservationId);
                if (!submitted)
                {
                    GroupReservation? currentGroupReservation =
                        await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
                    if (currentGroupReservation is null)
                        throw new GroupReservationNotFoundException();

                    if (currentGroupReservation.Status == GroupReservationStatus.Submitted)
                        throw new GroupReservationSubmittedException();

                    throw new GroupReservationExpiredException();
                }

                return consolidatedDishes;
            }
            catch (OrderConcurrencyException) when (attempt < maxOrderConcurrencyRetries)
            {
                Order? refreshedOrder;
                try
                {
                    refreshedOrder = await configuredOrderRepository.GetOrderByIdAsync(order.OrderId, order.CustomerId);
                }
                catch (Exception refreshException)
                {
                    await TryRestoreOpenPreservingExceptionAsync(groupReservationId, refreshException);
                    throw;
                }

                if (refreshedOrder is null)
                {
                    var exception = new ValidationException("Order not found.");
                    await TryRestoreOpenPreservingExceptionAsync(groupReservationId, exception);
                    throw exception;
                }

                order = refreshedOrder;
            }
        }
    }

    private async Task TryRestoreOpenPreservingExceptionAsync(
        string groupReservationId,
        Exception originalException)
    {
        try
        {
            await groupReservationRepository.TryRestoreOpenAfterFailedSubmissionAsync(groupReservationId);
        }
        catch (Exception restoreException)
        {
            originalException.Data["GroupReservationRestoreException"] = restoreException;
        }
    }

    private async Task RestoreOpenBeforeValidationAsync(string groupReservationId)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (await groupReservationRepository.TryRestoreOpenAfterFailedSubmissionAsync(groupReservationId))
                return;

            GroupReservation? currentGroup =
                await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
            if (currentGroup is null)
                throw new GroupReservationNotFoundException();

            if (currentGroup.Status == GroupReservationStatus.Submitted)
                throw new GroupReservationSubmittedException();

            if (currentGroup.Status == GroupReservationStatus.Closed)
                throw new GroupReservationExpiredException();

            if (currentGroup.Status == GroupReservationStatus.Open)
                return;
        }

        throw new InvalidOperationException(
            $"Group reservation {groupReservationId} could not be reopened after validation failed.");
    }

    private static List<OrderItem> MergeDishes(
        IEnumerable<OrderItem> existingDishes,
        IEnumerable<ConsolidatedDishResponse> submittedDishes)
    {
        List<OrderItem> mergedDishes = existingDishes
            .GroupBy(dish => dish.DishId)
            .Select(group => new OrderItem
            {
                DishId = group.Key,
                Quantity = group.Sum(dish => dish.Quantity),
                State = group.First().State
            })
            .ToList();

        foreach (ConsolidatedDishResponse dish in submittedDishes)
        {
            OrderItem? existingDish = mergedDishes.FirstOrDefault(item => item.DishId == dish.DishId);
            if (existingDish is not null)
            {
                existingDish.Quantity += dish.Quantity;
                continue;
            }

            mergedDishes.Add(new OrderItem
            {
                DishId = dish.DishId,
                Quantity = dish.Quantity,
                State = OrderItemState.Pending
            });
        }

        return mergedDishes;
    }

    private static void EnsureReservationOwner(string userId, Reservation reservation)
    {
        if (!string.Equals(reservation.CustomerId, userId, StringComparison.OrdinalIgnoreCase))
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
    }

    private async Task EnsureOwnerGuestSessionExistsAsync(string userId, string groupReservationId)
    {
        GroupReservation groupReservation =
            await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (!string.IsNullOrWhiteSpace(groupReservation.OwnerGuestSessionId))
        {
            GuestSession? linkedSession = await guestSessionRepository.GetByGuestSessionId(
                groupReservation.OwnerGuestSessionId);
            if (linkedSession is not null &&
                linkedSession.IsOwnerSession &&
                string.Equals(linkedSession.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(linkedSession.GroupReservationId, groupReservationId, StringComparison.Ordinal))
            {
                return;
            }

            throw new GuestSessionNotFoundException("Owner guest session was not found.");
        }

        string ownerGuestSessionId = $"owner-{groupReservationId}";
        GuestSession? existingOwnerSession =
            await guestSessionRepository.GetByGuestSessionId(ownerGuestSessionId);
        if (existingOwnerSession is not null &&
            (!existingOwnerSession.IsOwnerSession ||
             !string.Equals(existingOwnerSession.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(existingOwnerSession.GroupReservationId, groupReservationId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The owner guest session key is associated with a different session.");
        }

        if (existingOwnerSession is null)
        {
            await guestSessionRepository.SaveAsync(new GuestSession
            {
                Id = ownerGuestSessionId,
                GroupReservationId = groupReservationId,
                GuestName = "Owner",
                Dishes = [],
                IsOwnerSession = true,
                OwnerUserId = userId
            });
        }

        await groupReservationRepository.SetOwnerGuestSessionIdAsync(
            groupReservation.GroupReservationId,
            ownerGuestSessionId);
    }
}
