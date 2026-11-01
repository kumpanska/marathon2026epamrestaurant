using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Mappers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using APIHandler.Services.Notifications;

namespace APIHandler.Services.GuestSessions;

public class GuestSessionService(
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IReservationRepository reservationRepository,
    IUserRepository userRepository,
    ITableRepository tableRepository) : IGuestSessionService
{
    public async Task<bool> IsAuthorizedAsync(string guestSessionToken, string guestSessionId)
    {
        byte[] tokenBytes;
        try
        {
            tokenBytes = Convert.FromBase64String(guestSessionToken);
        }
        catch (FormatException)
        {
            return false;
        }

        string hash = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        GuestSession? guestSession = await guestSessionRepository.GetByTokenHash(hash);
        if (guestSession is null)
            return false;

        return guestSession.Id == guestSessionId;
    }

    public async Task<bool> IsOwnerSessionAuthorizedAsync(string userId, string guestSessionId)
    {
        GuestSession? guestSession = await guestSessionRepository.GetByGuestSessionId(guestSessionId);
        return guestSession is { IsOwnerSession: true } &&
            string.Equals(guestSession.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CreatGuestSessionResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request)
    {
        GroupReservation group = await GetGroupReservationAndValidateAsync(request.GroupReservationId);

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(group.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(group.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new GroupReservationExpiredException($"Associated reservation for group '{request.GroupReservationId}' not found.");

        if (reservation.Status != ReservationStatus.Reserved)
        {
            throw new GuestSessionExpiredException($"This pre-order session has expired or is no longer active. Current reservation status: '{reservation.Status}'.");
        }

        int joinedGuestsCount = await countTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException($"Guest limit exceeded. Maximum capacity is {reservation.GuestCount}, but {joinedGuestsCount} guests have already joined.");

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);

        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GuestName = request.GuestName,
            GroupReservationId = group.GroupReservationId,
            SessionTokenHash = Convert.ToBase64String(SHA256.HashData(tokenBytes)),
            Dishes = [],
        };

        await guestSessionRepository.SaveAsync(session);

        return new CreatGuestSessionResponse(session.Id, Convert.ToBase64String(tokenBytes));
    }

    public async Task<GuestMenuResponse> GetGuestMenuAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new ValidationException($"Guest session '{guestSessionId}' not found.");

        await ValidateGroupReservationId(session.GroupReservationId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var dishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dishesMapped = dishes
            .GroupBy(d => d.DishType)
            .ToDictionary(k => k.Key, GuestMenuDishMapper.Map);

        return new GuestMenuResponse(dishesMapped);
    }

    public async Task AddDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException($"Quantity ({quantity}) must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var locationDishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dish = locationDishes.FirstOrDefault(d => d.Id == dishId)
            ?? throw new ValidationException($"Dish '{dishId}' does not exist or is not available at this location.");

        if (dish.State != DishState.Available)
            throw new DishIsNotAvailableException($"Dish '{dishId}' is currently unavailable. State: '{dish.State}'.");

        session.Dishes ??= [];

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId);

        if (existingDish != null)
        {
            existingDish.Quantity += quantity;
        }
        else
        {
            session.Dishes.Add(new OrderItem
            {
                DishId = dishId,
                Quantity = quantity,
                State = OrderItemState.Pending
            });
        }

        if (!await guestSessionRepository.TryUpdateDishesWhileGroupOpenAsync(session))
            await ThrowForFailedSessionMutationAsync(session.Id, session.GroupReservationId);
    }

    public async Task RemoveDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException("Quantity must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        if (session.Dishes == null || session.Dishes.Count == 0)
            throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        existingDish.Quantity -= quantity;

        if (existingDish.Quantity <= 0)
        {
            session.Dishes.Remove(existingDish);
        }

        if (!await guestSessionRepository.TryUpdateDishesWhileGroupOpenAsync(session))
            await ThrowForFailedSessionMutationAsync(session.Id, session.GroupReservationId);
    }

    public async Task<GroupReservationStatusResponse> GetGroupReservationStatusAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new InvalidOperationException("Associated reservation not found.");

        int currentParticipants = await GetCurrentParticipantCountAsync(groupReservationId);

        return new GroupReservationStatusResponse
        {
            CurrentParticipants = currentParticipants,
            MaxCapacity = reservation.GuestCount,
            Status = group.Status == GroupReservationStatus.Submitting
                ? GroupReservationStatus.Open
                : group.Status
        };
    }

    public async Task<GetAllSessionsResponse> GetGroupSessionsAsync(string userId, string groupReservationId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);

        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);

        var dishIds = sessions
            .SelectMany(session => session.Dishes)
            .Select(item => item.DishId)
            .Distinct()
            .ToArray();

        var dishesById = (await dishRepository.GetDishesByIdsAsync(dishIds))
            .ToDictionary(dish => dish.Id);

        var participants = new List<Participant>();
        foreach (var session in sessions)
        {
            var items = session.Dishes.Select(item => new OrderItemResponse
            {
                Dish = DishMapper.ToDishResponse(dishesById[item.DishId]),
                Quantity = item.Quantity
            }).ToArray();

            decimal total = items.Sum(item => item.Quantity * item.Dish.Price);
            string name = session.GuestName;
            if (session.IsOwnerSession)
            {
                var user = await userRepository.GetUserByIdAsync(userId);
                name = user!.Attributes.First(attribute => attribute.Name == "given_name").Value;
            }

            participants.Add(new Participant(
                session.Id,
                session.IsOwnerSession ? ParticipantType.Owner : ParticipantType.Guest,
                name,
                items,
                total,
                session.IsSubmitted));
        }

        return new GetAllSessionsResponse(participants, participants.Sum(participant => participant.Total));
    }

    public async Task<GetAllSessionsResponse> DeleteGuestSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);

        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        if (session.GroupReservationId != groupReservationId)
        {
            throw new ValidationException("Guest session does not belong to this group reservation.");
        }

        if (session.IsOwnerSession)
        {
            throw new ActionForbiddenException("Owner sessions cannot be deleted.");
        }

        if (!await guestSessionRepository.TryDeleteWhileGroupOpenAsync(guestSessionId, groupReservationId))
        {
            GroupReservation? currentGroup =
                await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
            if (currentGroup is null)
                throw new GroupReservationNotFoundException();

            if (currentGroup.Status == GroupReservationStatus.Submitted)
                throw new GroupReservationSubmittedException();

            if (currentGroup.Status != GroupReservationStatus.Open)
                throw new GroupReservationExpiredException();

            throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");
        }

        // The group-session index is eventually consistent. Read the base table
        // after deletion so the returned snapshot cannot include the deleted session.
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdStronglyConsistentAsync(groupReservationId);

        var dishIds = sessions
            .SelectMany(s => s.Dishes)
            .Select(s => s.DishId)
            .Distinct()
            .ToArray();

        var allDishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var dishesById = allDishes.ToDictionary(d => d.Id);

        var participants = new List<Participant>();

        foreach (var guestSession in sessions)
        {
            var items = guestSession.Dishes.Select(d => new OrderItemResponse
            {
                Dish = DishMapper.ToDishResponse(dishesById[d.DishId]),
                Quantity = d.Quantity
            }).ToArray();

            decimal total = items.Sum(i => i.Quantity * i.Dish.Price);
            string name = guestSession.GuestName;
            if (guestSession.IsOwnerSession)
            {
                var user = await userRepository.GetUserByIdAsync(userId);
                name = user!.Attributes.First(attribute => attribute.Name == "given_name").Value;
            }

            var participant = new Participant(
                guestSession.Id,
                guestSession.IsOwnerSession ? ParticipantType.Owner : ParticipantType.Guest,
                name,
                items,
                total,
                guestSession.IsSubmitted);
            participants.Add(participant);
        }

        return new GetAllSessionsResponse(participants, participants.Sum(p => p.Total));
    }
    public async Task SubmitCartAsync(string guestSessionId)
    {
        GuestSession guestSession = await GetGuestSessionAsync(guestSessionId);

        if (guestSession.Dishes.Count > 0)
            await ValidateDishesAsync(guestSession.Dishes);

        bool submitted = await guestSessionRepository.TryMarkSubmittedAsync(
            guestSession.Id,
            guestSession.GroupReservationId);
        if (submitted)
            return;

        GroupReservation? currentGroup =
            await groupReservationRepository.GetGroupReservationByIdAsync(guestSession.GroupReservationId);
        if (currentGroup is null)
            throw new GroupReservationNotFoundException();

        if (currentGroup.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (currentGroup.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException();

        GuestSession? currentSession =
            await guestSessionRepository.GetByGuestSessionId(guestSession.Id);
        if (currentSession is null)
            throw new GuestSessionNotFoundException($"Guest session {guestSession.Id} not found.");

        if (currentSession.IsSubmitted)
            throw new GuestSessionSubmittedException();

        throw new InvalidOperationException(
            $"Guest session {guestSession.Id} could not be submitted while its group remained open.");
    }

    private async Task<GroupReservation> GetGroupReservationAndValidateAsync(string groupReservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        ValidateGroupReservation(group);
        return group;
    }

    private async Task<int> GetCurrentParticipantCountAsync(string groupReservationId)
    {
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);

        return sessions?.Count ?? 0;
    }

    private async Task ValidateGroupReservationId(string groupReservationId) => await GetGroupReservationAndValidateAsync(groupReservationId);

    private static void ValidateGroupReservation([NotNull] GroupReservation? group)
    {
        if (group is null)
            throw new GroupReservationNotFoundException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status == GroupReservationStatus.Submitting)
            throw new GroupReservationExpiredException();
    }

    private async Task<GuestSession> GetGuestSessionAsync(string sessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(sessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {sessionId} not found.");

        if (session.IsSubmitted)
            throw new GuestSessionSubmittedException();

        await ValidateGroupReservationId(session.GroupReservationId);

        return session;
    }

    private async Task ThrowForFailedSessionMutationAsync(string guestSessionId, string groupReservationId)
    {
        GroupReservation? currentGroup =
            await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        if (currentGroup is null)
            throw new GroupReservationNotFoundException();

        if (currentGroup.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (currentGroup.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException();

        GuestSession? currentSession =
            await guestSessionRepository.GetByGuestSessionId(guestSessionId);
        if (currentSession is null)
            throw new GuestSessionNotFoundException($"Guest session {guestSessionId} not found.");

        if (currentSession.IsSubmitted)
            throw new GuestSessionSubmittedException();

        throw new InvalidOperationException(
            $"Guest session {guestSessionId} could not be updated while its group remained open.");
    }

    private async Task ValidateDishesAsync(IEnumerable<OrderItem> orders)
    {
        IEnumerable<string> dishIds = orders.Select(o => o.DishId).ToArray();
        IEnumerable<Dish> dishes = await dishRepository.GetDishesByIdsAsync(dishIds);

        if (dishes.FirstOrDefault(d => d.State != DishState.Available) is Dish dish)
            throw new DishIsNotAvailableException($"The dish '{dish.Name}' ({dish.Id}) is not available.");
    }

    private async Task<string> GetLocationIdForGroupAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        var table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new ValidationException("Associated table not found.");

        return table.LocationId;
    }

    
    private async Task ValidateReservationOwnerAsync(string groupReservationId, string userId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        if (reservation.CustomerId != userId)
        {
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        }
    }

    public async Task CreateGroupOwnerSessionAsync(string userId, string groupReservationId)
    {
        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GroupReservationId = groupReservationId,
            GuestName = "Owner",
            Dishes = [],
            IsOwnerSession = true,
            OwnerUserId = userId,
        };

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GetGuestSessionResponse> GetGuestSessionInfoAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        var dishIds = session.Dishes
            .Select(s => s.DishId)
            .Distinct()
            .ToArray();

        var allDishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var dishesById = allDishes.ToDictionary(d => d.Id);

        var items = session.Dishes.Select(d => new OrderItemResponse
        {
            Dish = DishMapper.ToDishResponse(dishesById[d.DishId]),
            Quantity = d.Quantity
        }).ToArray();

        decimal total = items.Sum(i => i.Quantity * i.Dish.Price);

        string name = session.GuestName;
        if (session.IsOwnerSession)
        {
            var groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(session.GroupReservationId);
            var reservation = await reservationRepository.GetReservationByIdAsync(groupReservation!.ReservationId);
            var user = await userRepository.GetUserByIdAsync(reservation!.CustomerId!);
            name = user!.Attributes.First(a => a.Name == "given_name").Value;
        }

        return new GetGuestSessionResponse(name, items, total, session.IsSubmitted);
    }

}
