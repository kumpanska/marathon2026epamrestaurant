using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

public class GetGroupSessionsServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepository = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepository = new();
    private readonly Mock<IDishRepository> _dishRepository = new();
    private readonly Mock<IReservationRepository> _reservationRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ITableRepository> _tableRepository = new();

    private readonly GuestSessionService _service;

    public GetGroupSessionsServiceTests()
    {
        _service = new GuestSessionService(
            _groupReservationRepository.Object,
            _guestSessionRepository.Object,
            _dishRepository.Object,
            _reservationRepository.Object,
            _userRepository.Object,
            _tableRepository.Object);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_OwnerRequestsGroup_ReturnsParticipantsMealsAndTotals()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        var groupReservation = new GroupReservation
        {
            GroupReservationId = groupReservationId,
            ReservationId = "reservation-123",
            ReservationToken = "invite-token"
        };
        var reservation = new Reservation
        {
            Id = groupReservation.ReservationId,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = userId
        };
        var ownerDish = new Dish
        {
            Id = "dish-owner",
            Name = "Owner's meal",
            Price = 12.50m,
            DishType = DishType.MainCourse,
            State = DishState.Available,
            Description = "Owner meal description",
            Weight = "300g",
            Calories = "500",
            Proteins = "20g",
            Fats = "15g",
            Carbohydrates = "40g",
            Vitamins = "B"
        };
        var guestDish = new Dish
        {
            Id = "dish-guest",
            Name = "Guest's meal",
            Price = 7.25m,
            DishType = DishType.Appetizer,
            State = DishState.Available,
            Description = "Guest meal description",
            Weight = "150g",
            Calories = "250",
            Proteins = "10g",
            Fats = "5g",
            Carbohydrates = "30g",
            Vitamins = "C"
        };
        var ownerSession = new GuestSession
        {
            Id = "session-owner",
            GroupReservationId = groupReservationId,
            GuestName = "Owner",
            IsOwnerSession = true,
            IsSubmitted = true,
            Dishes = [new OrderItem { DishId = ownerDish.Id, Quantity = 2, State = OrderItemState.Pending }]
        };
        var guestSession = new GuestSession
        {
            Id = "session-guest",
            GroupReservationId = groupReservationId,
            GuestName = "Sam",
            IsSubmitted = false,
            Dishes = [new OrderItem { DishId = guestDish.Id, Quantity = 3, State = OrderItemState.Pending }]
        };

        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepository
            .Setup(repository => repository.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(reservation);
        _guestSessionRepository
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservationId))
            .ReturnsAsync(new List<GuestSession> { ownerSession, guestSession });
        _dishRepository
            .Setup(repository => repository.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<Dish>)new[] { ownerDish, guestDish });
        _userRepository
            .Setup(repository => repository.GetUserByIdAsync(userId))
            .ReturnsAsync(new UserType
            {
                Attributes = [new AttributeType { Name = "given_name", Value = "Mia" }]
            });

        var result = await _service.GetGroupSessionsAsync(userId, groupReservationId);

        var participants = result.Participants.ToArray();
        Assert.Equal(2, participants.Length);

        var owner = Assert.Single(participants, participant => participant.Type == ParticipantType.Owner);
        Assert.Equal(ownerSession.Id, owner.Id);
        Assert.Equal("Mia", owner.Name);
        Assert.True(owner.IsSubmitted);
        Assert.Equal(25.00m, owner.Total);
        var ownerItem = Assert.Single(owner.Items);
        Assert.Equal(ownerDish.Id, ownerItem.Dish.Id);
        Assert.Equal(ownerDish.Name, ownerItem.Dish.Name);
        Assert.Equal(ownerDish.Price, ownerItem.Dish.Price);
        Assert.Equal(2, ownerItem.Quantity);

        var guest = Assert.Single(participants, participant => participant.Type == ParticipantType.Guest);
        Assert.Equal(guestSession.Id, guest.Id);
        Assert.Equal("Sam", guest.Name);
        Assert.False(guest.IsSubmitted);
        Assert.Equal(21.75m, guest.Total);
        var guestItem = Assert.Single(guest.Items);
        Assert.Equal(guestDish.Id, guestItem.Dish.Id);
        Assert.Equal(guestDish.Name, guestItem.Dish.Name);
        Assert.Equal(guestDish.Price, guestItem.Dish.Price);
        Assert.Equal(3, guestItem.Quantity);
        Assert.Equal(46.75m, result.Total);

        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(groupReservationId),
            Times.Once);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_UserDoesNotOwnReservation_ThrowsForbiddenWithoutLoadingSessions()
    {
        const string userId = "different-user";
        const string groupReservationId = "group-123";
        var groupReservation = new GroupReservation
        {
            GroupReservationId = groupReservationId,
            ReservationId = "reservation-123",
            ReservationToken = "invite-token"
        };
        var reservation = new Reservation
        {
            Id = groupReservation.ReservationId,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = "actual-owner"
        };

        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepository
            .Setup(repository => repository.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(
            () => _service.GetGroupSessionsAsync(userId, groupReservationId));

        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_GroupDoesNotExist_ThrowsNotFound()
    {
        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync("missing-group"))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(
            () => _service.GetGroupSessionsAsync("owner-user", "missing-group"));

        _reservationRepository.Verify(
            repository => repository.GetReservationByIdAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_OwnerDeletesSession_ReturnsUpdatedSnapshotWithRecalculatedTotals()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        const string deletedSessionId = "session-to-delete";
        var groupReservation = new GroupReservation
        {
            GroupReservationId = groupReservationId,
            ReservationId = "reservation-123",
            ReservationToken = "invite-token"
        };
        var reservation = new Reservation
        {
            Id = groupReservation.ReservationId,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = userId
        };
        var deletedSession = new GuestSession
        {
            Id = deletedSessionId,
            GroupReservationId = groupReservationId,
            GuestName = "Removed guest",
            Dishes = []
        };
        var ownerDish = new Dish
        {
            Id = "dish-owner",
            Name = "Owner meal",
            Price = 12.50m,
            DishType = DishType.MainCourse,
            State = DishState.Available,
            Description = "Owner meal description",
            Weight = "300g",
            Calories = "500",
            Proteins = "20g",
            Fats = "15g",
            Carbohydrates = "40g",
            Vitamins = "B"
        };
        var guestDish = new Dish
        {
            Id = "dish-guest",
            Name = "Guest meal",
            Price = 7.25m,
            DishType = DishType.Appetizer,
            State = DishState.Available,
            Description = "Guest meal description",
            Weight = "150g",
            Calories = "250",
            Proteins = "10g",
            Fats = "5g",
            Carbohydrates = "30g",
            Vitamins = "C"
        };
        var ownerSession = new GuestSession
        {
            Id = "session-owner",
            GroupReservationId = groupReservationId,
            GuestName = "Owner",
            IsOwnerSession = true,
            IsSubmitted = true,
            Dishes = [new OrderItem { DishId = ownerDish.Id, Quantity = 2, State = OrderItemState.Pending }]
        };
        var remainingGuestSession = new GuestSession
        {
            Id = "session-remaining",
            GroupReservationId = groupReservationId,
            GuestName = "Sam",
            IsSubmitted = false,
            Dishes = [new OrderItem { DishId = guestDish.Id, Quantity = 3, State = OrderItemState.Pending }]
        };

        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepository
            .Setup(repository => repository.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(reservation);
        _guestSessionRepository
            .Setup(repository => repository.GetByGuestSessionId(deletedSessionId))
            .ReturnsAsync(deletedSession);
        _guestSessionRepository
            .Setup(repository => repository.TryDeleteWhileGroupOpenAsync(deletedSessionId, groupReservationId))
            .ReturnsAsync(true);
        _guestSessionRepository
            .Setup(repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(groupReservationId))
            .ReturnsAsync([ownerSession, remainingGuestSession]);
        _dishRepository
            .Setup(repository => repository.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<Dish>)[ownerDish, guestDish]);
        _userRepository
            .Setup(repository => repository.GetUserByIdAsync(userId))
            .ReturnsAsync(new UserType
            {
                Attributes = [new AttributeType { Name = "given_name", Value = "Mia" }]
            });

        GetAllSessionsResponse result = await _service.DeleteGuestSessionAsync(
            userId,
            groupReservationId,
            deletedSessionId);

        Participant[] participants = result.Participants.ToArray();
        Assert.Equal(2, participants.Length);
        Assert.DoesNotContain(participants, participant => participant.Id == deletedSessionId);

        Participant owner = Assert.Single(participants, participant => participant.Type == ParticipantType.Owner);
        Assert.Equal(ownerSession.Id, owner.Id);
        Assert.Equal("Mia", owner.Name);
        Assert.True(owner.IsSubmitted);
        Assert.Equal(25.00m, owner.Total);
        OrderItemResponse ownerItem = Assert.Single(owner.Items);
        Assert.Equal(ownerDish.Id, ownerItem.Dish.Id);
        Assert.Equal(ownerDish.Name, ownerItem.Dish.Name);
        Assert.Equal(ownerDish.Price, ownerItem.Dish.Price);
        Assert.Equal(2, ownerItem.Quantity);

        Participant guest = Assert.Single(participants, participant => participant.Type == ParticipantType.Guest);
        Assert.Equal(remainingGuestSession.Id, guest.Id);
        Assert.Equal("Sam", guest.Name);
        Assert.False(guest.IsSubmitted);
        Assert.Equal(21.75m, guest.Total);
        OrderItemResponse guestItem = Assert.Single(guest.Items);
        Assert.Equal(guestDish.Id, guestItem.Dish.Id);
        Assert.Equal(guestDish.Name, guestItem.Dish.Name);
        Assert.Equal(guestDish.Price, guestItem.Dish.Price);
        Assert.Equal(3, guestItem.Quantity);
        Assert.Equal(46.75m, result.Total);

        _guestSessionRepository.Verify(
            repository => repository.TryDeleteWhileGroupOpenAsync(deletedSessionId, groupReservationId),
            Times.Once);
        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(groupReservationId),
            Times.Once);
        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_SessionDoesNotExist_ThrowsNotFoundWithoutDeleting()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        const string guestSessionId = "missing-session";
        SetupReservationOwner(groupReservationId, userId);
        _guestSessionRepository
            .Setup(repository => repository.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(
            () => _service.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId));

        _guestSessionRepository.Verify(
            repository => repository.TryDeleteWhileGroupOpenAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_SessionBelongsToAnotherGroup_ThrowsValidationWithoutDeleting()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        const string guestSessionId = "session-other-group";
        SetupReservationOwner(groupReservationId, userId);
        _guestSessionRepository
            .Setup(repository => repository.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync(new GuestSession
            {
                Id = guestSessionId,
                GroupReservationId = "different-group",
                GuestName = "Sam",
                Dishes = []
            });

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId));

        _guestSessionRepository.Verify(
            repository => repository.TryDeleteWhileGroupOpenAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_OwnerSession_ThrowsForbiddenWithoutDeleting()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        const string ownerSessionId = "owner-session";
        SetupReservationOwner(groupReservationId, userId);
        _guestSessionRepository
            .Setup(repository => repository.GetByGuestSessionId(ownerSessionId))
            .ReturnsAsync(new GuestSession
            {
                Id = ownerSessionId,
                GroupReservationId = groupReservationId,
                GuestName = "Owner",
                IsOwnerSession = true,
                Dishes = []
            });

        await Assert.ThrowsAsync<ActionForbiddenException>(
            () => _service.DeleteGuestSessionAsync(userId, groupReservationId, ownerSessionId));

        _guestSessionRepository.Verify(
            repository => repository.TryDeleteWhileGroupOpenAsync(ownerSessionId, groupReservationId),
            Times.Never);
        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_GroupIsClosed_ThrowsExpiredConflict()
    {
        const string userId = "owner-user";
        const string groupReservationId = "group-123";
        const string guestSessionId = "session-123";
        SetupReservationOwner(groupReservationId, userId);
        _guestSessionRepository
            .Setup(repository => repository.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync(new GuestSession
            {
                Id = guestSessionId,
                GroupReservationId = groupReservationId,
                GuestName = "Sam",
                Dishes = []
            });
        _guestSessionRepository
            .Setup(repository => repository.TryDeleteWhileGroupOpenAsync(guestSessionId, groupReservationId))
            .ReturnsAsync(false);
        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(new GroupReservation
            {
                GroupReservationId = groupReservationId,
                ReservationId = "reservation-123",
                ReservationToken = "invite-token",
                Status = GroupReservationStatus.Closed
            });

        await Assert.ThrowsAsync<GroupReservationExpiredException>(
            () => _service.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId));

        _guestSessionRepository.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_CallerDoesNotOwnReservation_ThrowsForbiddenWithoutReadingSession()
    {
        const string userId = "different-user";
        const string groupReservationId = "group-123";
        SetupReservationOwner(groupReservationId, "actual-owner");

        await Assert.ThrowsAsync<ActionForbiddenException>(
            () => _service.DeleteGuestSessionAsync(userId, groupReservationId, "session-123"));

        _guestSessionRepository.Verify(
            repository => repository.GetByGuestSessionId(It.IsAny<string>()),
            Times.Never);
        _guestSessionRepository.Verify(
            repository => repository.TryDeleteWhileGroupOpenAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    private void SetupReservationOwner(string groupReservationId, string ownerId)
    {
        var groupReservation = new GroupReservation
        {
            GroupReservationId = groupReservationId,
            ReservationId = "reservation-123",
            ReservationToken = "invite-token"
        };
        _groupReservationRepository
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepository
            .Setup(repository => repository.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(new Reservation
            {
                Id = groupReservation.ReservationId,
                TableId = "table-1",
                WaiterId = "waiter-1",
                CustomerId = ownerId
            });
    }
}
