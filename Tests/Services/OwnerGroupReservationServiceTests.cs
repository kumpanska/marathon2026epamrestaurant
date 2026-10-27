using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using Moq;

namespace APIHandler.Tests.Services;

public class OwnerGroupReservationServiceTests
{
    private const string OwnerId = "owner-123";
    private const string ReservationId = "reservation-123";
    private const string GroupReservationId = "group-123";

    private readonly Mock<IGroupReservationRepository> _groupReservations = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IGuestSessionRepository> _guestSessions = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly GroupReservationService _service;

    public OwnerGroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservations.Object,
            Mock.Of<ITableRepository>(),
            _reservations.Object,
            _guestSessions.Object,
            Mock.Of<IUserService>(),
            Mock.Of<ILocationService>(),
            _orders.Object);
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_ReturnsSessionOwnedByCaller()
    {
        GroupReservation groupReservation = CreateGroupReservation();
        groupReservation.OwnerGuestSessionId = "owner-session";
        Reservation reservation = CreateReservation();
        GuestSession ownerSession = new()
        {
            Id = "owner-session",
            GroupReservationId = GroupReservationId,
            GuestName = "Owner",
            Dishes = [],
            IsOwnerSession = true,
            OwnerUserId = OwnerId.ToUpperInvariant()
        };
        _reservations
            .Setup(repository => repository.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(reservation);
        _groupReservations
            .Setup(repository => repository.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync(groupReservation);
        _groupReservations
            .Setup(repository => repository.GetGroupReservationByIdAsync(GroupReservationId))
            .ReturnsAsync(groupReservation);
        _guestSessions
            .Setup(repository => repository.GetByGuestSessionId("owner-session"))
            .ReturnsAsync(ownerSession);

        string sessionId = await _service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId);

        Assert.Equal("owner-session", sessionId);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_AggregatesOnlySubmittedSessionsAndSubmitsThroughOrderRepository()
    {
        GroupReservation groupReservation = CreateGroupReservation();
        Reservation reservation = CreateReservation();
        var order = new Order
        {
            OrderId = "order-123",
            CustomerId = OwnerId,
            ReservationId = ReservationId,
            ReservationAddress = "Address",
            ReservationTime = "Time",
            Dishes = [],
            Status = OrderStatus.Created
        };
        var sessions = new List<GuestSession>
        {
            CreateGuestSession("submitted-1", isSubmitted: true,
                new OrderItem { DishId = "dish-1", Quantity = 2, State = OrderItemState.Pending },
                new OrderItem { DishId = "dish-2", Quantity = 1, State = OrderItemState.Confirmed }),
            CreateGuestSession("submitted-2", isSubmitted: true,
                new OrderItem { DishId = "dish-1", Quantity = 3, State = OrderItemState.Pending }),
            CreateGuestSession("not-submitted", isSubmitted: false,
                new OrderItem { DishId = "dish-1", Quantity = 100, State = OrderItemState.Pending },
                new OrderItem { DishId = "dish-3", Quantity = 9, State = OrderItemState.Pending })
        };
        SetupSubmission(groupReservation, reservation, sessions, order);

        List<ConsolidatedDishResponse> result = await _service.SubmitGroupReservationAsync(
            OwnerId,
            GroupReservationId,
            ReservationId);

        Assert.Equal(
            new[] { new ConsolidatedDishResponse("dish-1", 5), new ConsolidatedDishResponse("dish-2", 1) },
            result);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(
            new[]
            {
                new OrderItem { DishId = "dish-1", Quantity = 5, State = OrderItemState.Pending },
                new OrderItem { DishId = "dish-2", Quantity = 1, State = OrderItemState.Pending }
            },
            order.Dishes,
            OrderItemComparer.Instance);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(order, GroupReservationId),
            Times.Once);
        _groupReservations.Verify(
            repository => repository.TryBeginSubmissionAsync(GroupReservationId),
            Times.Once);
        _guestSessions.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(GroupReservationId),
            Times.Once);
        _guestSessions.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(GroupReservationId),
            Times.Never);
        _orders.Verify(repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservations.Verify(
            repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenGroupReservationIsAlreadySubmitted_ThrowsWithoutChangingOrder()
    {
        GroupReservation groupReservation = CreateGroupReservation();
        groupReservation.Status = GroupReservationStatus.Submitted;
        Reservation reservation = CreateReservation();
        var order = new Order
        {
            OrderId = "order-123",
            CustomerId = OwnerId,
            ReservationId = ReservationId,
            ReservationAddress = "Address",
            ReservationTime = "Time",
            Dishes =
            [
                new OrderItem { DishId = "dish-1", Quantity = 4, State = OrderItemState.Confirmed },
                new OrderItem { DishId = "dish-2", Quantity = 2, State = OrderItemState.Pending }
            ],
            Status = OrderStatus.SubmittedByCustomer
        };
        OrderItem[] originalDishes = order.Dishes
            .Select(dish => new OrderItem { DishId = dish.DishId, Quantity = dish.Quantity, State = dish.State })
            .ToArray();
        SetupSubmission(
            groupReservation,
            reservation,
            [CreateGuestSession("submitted", isSubmitted: true,
                new OrderItem { DishId = "dish-1", Quantity = 100, State = OrderItemState.Pending })],
            order);

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));

        Assert.Equal(GroupReservationStatus.Submitted, groupReservation.Status);
        Assert.Equal("order-123", order.OrderId);
        Assert.Equal(OwnerId, order.CustomerId);
        Assert.Equal(ReservationId, order.ReservationId);
        Assert.Equal("Address", order.ReservationAddress);
        Assert.Equal("Time", order.ReservationTime);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(originalDishes, order.Dishes, OrderItemComparer.Instance);
        _guestSessions.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(It.IsAny<string>()),
            Times.Never);
        _orders.Verify(
            repository => repository.GetOrderByReservationIdAsync(It.IsAny<string>()),
            Times.Never);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(
                It.IsAny<Order>(),
                It.IsAny<string>()),
            Times.Never);
        _groupReservations.Verify(
            repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenCallerIsNotReservationOwner_ThrowsForbidden()
    {
        GroupReservation groupReservation = CreateGroupReservation();
        Reservation reservation = CreateReservation();
        reservation.CustomerId = "another-user";
        SetupSubmission(groupReservation, reservation, [], order: null);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));

        _guestSessions.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenSubmissionWasAlreadyClaimed_ResumesWithoutClaimingAgain()
    {
        GroupReservation groupReservation = CreateGroupReservation();
        groupReservation.Status = GroupReservationStatus.Submitting;
        var order = new Order
        {
            OrderId = "order-123",
            CustomerId = OwnerId,
            ReservationId = ReservationId,
            ReservationAddress = "Address",
            ReservationTime = "Time",
            Dishes = [],
            Status = OrderStatus.Created
        };
        SetupSubmission(
            groupReservation,
            CreateReservation(),
            [
                CreateGuestSession(
                    "submitted",
                    isSubmitted: true,
                    new OrderItem { DishId = "dish-1", Quantity = 2, State = OrderItemState.Pending })
            ],
            order);

        List<ConsolidatedDishResponse> result = await _service.SubmitGroupReservationAsync(
            OwnerId,
            GroupReservationId,
            ReservationId);

        Assert.Equal(new[] { new ConsolidatedDishResponse("dish-1", 2) }, result);
        _groupReservations.Verify(
            repository => repository.TryBeginSubmissionAsync(GroupReservationId),
            Times.Never);
        _guestSessions.Verify(
            repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(GroupReservationId),
            Times.Once);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(order, GroupReservationId),
            Times.Once);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenGroupReservationIsMissing_ThrowsNotFound()
    {
        _groupReservations
            .Setup(repository => repository.GetGroupReservationByIdAsync(GroupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenReservationIsMissing_ThrowsNotFound()
    {
        _groupReservations
            .Setup(repository => repository.GetGroupReservationByIdAsync(GroupReservationId))
            .ReturnsAsync(CreateGroupReservation());
        _reservations
            .Setup(repository => repository.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenNoSessionWasSubmitted_ThrowsValidation()
    {
        SetupSubmission(CreateGroupReservation(), CreateReservation(), [], order: null);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));

        Assert.Equal("No submitted guest sessions were found.", exception.Message);
        _groupReservations.Verify(
            repository => repository.TryBeginSubmissionAsync(GroupReservationId),
            Times.Once);
        _groupReservations.Verify(
            repository => repository.TryRestoreOpenAfterFailedSubmissionAsync(GroupReservationId),
            Times.Once);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(
                It.IsAny<Order>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenSubmittedSessionsHaveNoDishes_ThrowsValidation()
    {
        SetupSubmission(
            CreateGroupReservation(),
            CreateReservation(),
            [CreateGuestSession("submitted-empty", isSubmitted: true)],
            order: null);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));

        Assert.Equal("Submitted guest sessions contain no dishes.", exception.Message);
        _groupReservations.Verify(
            repository => repository.TryRestoreOpenAfterFailedSubmissionAsync(GroupReservationId),
            Times.Once);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(
                It.IsAny<Order>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_WhenOrderIsMissing_ThrowsValidation()
    {
        SetupSubmission(
            CreateGroupReservation(),
            CreateReservation(),
            [CreateGuestSession(
                "submitted",
                isSubmitted: true,
                new OrderItem { DishId = "dish-1", Quantity = 1, State = OrderItemState.Pending })],
            order: null);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId));

        Assert.Equal("Order not found.", exception.Message);
        _orders.Verify(
            repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(
                It.IsAny<Order>(),
                It.IsAny<string>()),
            Times.Never);
        _groupReservations.Verify(
            repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()),
            Times.Never);
    }

    private void SetupSubmission(
        GroupReservation groupReservation,
        Reservation reservation,
        List<GuestSession> sessions,
        Order? order)
    {
        _groupReservations
            .Setup(repository => repository.GetGroupReservationByIdAsync(GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservations
            .Setup(repository => repository.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(reservation);
        _guestSessions
            .Setup(repository => repository.GetAllByGroupReservationIdStronglyConsistentAsync(GroupReservationId))
            .ReturnsAsync(sessions);
        _orders
            .Setup(repository => repository.GetOrderByReservationIdAsync(ReservationId))
            .ReturnsAsync(order);
        _orders
            .Setup(repository => repository.TrySaveOrderAndSubmitGroupReservationAsync(
                It.IsAny<Order>(),
                GroupReservationId))
            .ReturnsAsync(true);
        _groupReservations
            .Setup(repository => repository.TryBeginSubmissionAsync(GroupReservationId))
            .ReturnsAsync(true);
        _groupReservations
            .Setup(repository => repository.TryRestoreOpenAfterFailedSubmissionAsync(GroupReservationId))
            .ReturnsAsync(true);
    }

    private static GroupReservation CreateGroupReservation() =>
        new()
        {
            GroupReservationId = GroupReservationId,
            ReservationId = ReservationId,
            ReservationToken = "token",
            Status = GroupReservationStatus.Open
        };

    private static Reservation CreateReservation() =>
        new()
        {
            Id = ReservationId,
            TableId = "table-123",
            CustomerId = OwnerId,
            WaiterId = "waiter-123"
        };

    private static GuestSession CreateGuestSession(
        string id,
        bool isSubmitted,
        params OrderItem[] dishes) =>
        new()
        {
            Id = id,
            GroupReservationId = GroupReservationId,
            GuestName = id,
            Dishes = [.. dishes],
            IsSubmitted = isSubmitted
        };

    private sealed class OrderItemComparer : IEqualityComparer<OrderItem>
    {
        public static OrderItemComparer Instance { get; } = new();

        public bool Equals(OrderItem? left, OrderItem? right) =>
            left?.DishId == right?.DishId &&
            left?.Quantity == right?.Quantity &&
            left?.State == right?.State;

        public int GetHashCode(OrderItem item) =>
            HashCode.Combine(item.DishId, item.Quantity, item.State);
    }
}
