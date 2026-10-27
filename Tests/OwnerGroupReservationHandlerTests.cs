using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Handlers.Reservations;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.Reservations;
using APIHandler.Services.GroupReservations;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests;

public class OwnerGroupReservationHandlerTests
{
    private const string OwnerId = "owner-123";
    private const string ReservationId = "reservation-123";
    private const string GroupReservationId = "group-123";

    private readonly Mock<IGroupReservationService> _groupReservationService = new();
    private readonly Mock<ILambdaContext> _lambdaContext = new();
    private readonly Mock<ILambdaLogger> _logger = new();

    public OwnerGroupReservationHandlerTests()
    {
        _lambdaContext.Setup(context => context.Logger).Returns(_logger.Object);
    }

    [Fact]
    public async Task GetOwnerGuestSessionId_ReturnsSessionIdForAuthenticatedOwner()
    {
        const string sessionId = "session-123";
        _groupReservationService
            .Setup(service => service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId))
            .ReturnsAsync(sessionId);

        var handler = new GetOwnerGuestSessionIdHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateRequest("reservationId", ReservationId, includeAuthenticatedUser: true),
            _lambdaContext.Object);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(
            sessionId,
            JsonSerializer.Deserialize<GuestSessionIdResponse>(
                response.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.GuestSessionId);
        _groupReservationService.Verify(
            service => service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId),
            Times.Once);
    }

    [Fact]
    public async Task GetOwnerGuestSessionId_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        _groupReservationService
            .Setup(service => service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId))
            .ThrowsAsync(new ActionForbiddenException("Only the reservation owner can perform this action."));

        var handler = new GetOwnerGuestSessionIdHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateRequest("reservationId", ReservationId, includeAuthenticatedUser: true),
            _lambdaContext.Object);

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task GetOwnerGuestSessionId_WhenReservationIsMissing_ReturnsNotFound()
    {
        _groupReservationService
            .Setup(service => service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId))
            .ThrowsAsync(new ReservationNotFoundException("Reservation was not found."));

        var handler = new GetOwnerGuestSessionIdHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateRequest("reservationId", ReservationId, includeAuthenticatedUser: true),
            _lambdaContext.Object);

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task GetOwnerGuestSessionId_WhenUnauthenticated_DoesNotCallService()
    {
        var handler = new GetOwnerGuestSessionIdHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateRequest("reservationId", ReservationId, includeAuthenticatedUser: false),
            _lambdaContext.Object);

        Assert.Equal(401, response.StatusCode);
        _groupReservationService.Verify(
            service => service.GetOwnerGuestSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservation_ValidRequestSubmitsReservationAndReturnsAggregatedDishes()
    {
        var aggregatedDishes = new List<ConsolidatedDishResponse>
        {
            new("dish-1", 4),
            new("dish-2", 1)
        };
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ReturnsAsync(aggregatedDishes);

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(
            aggregatedDishes,
            JsonSerializer.Deserialize<List<ConsolidatedDishResponse>>(
                response.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId),
            Times.Once);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenReservationDoesNotExist_ReturnsNotFound()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ThrowsAsync(new ReservationNotFoundException("Reservation was not found."));

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenGroupReservationDoesNotExist_ReturnsNotFound()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ThrowsAsync(new APIHandler.Exceptions.GroupReservationNotFoundException());

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenNoSubmittedSessions_ReturnsBadRequest()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ThrowsAsync(new ValidationException("No submitted guest sessions were found."));

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenSubmittedSessionsHaveNoDishes_ReturnsBadRequest()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ThrowsAsync(new ValidationException("Submitted guest sessions contain no dishes."));

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(400, response.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId),
            Times.Once);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenGroupReservationIsAlreadySubmitted_ReturnsBadRequest()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId))
            .ThrowsAsync(new GroupReservationSubmittedException());

        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest($"{{\"reservationId\":\"{ReservationId}\"}}"),
            _lambdaContext.Object);

        Assert.Equal(400, response.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(OwnerId, GroupReservationId, ReservationId),
            Times.Once);
    }

    [Fact]
    public async Task SubmitGroupReservation_WhenRequestHasNoReservationId_DoesNotCallService()
    {
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        var response = await handler.HandleRequest(
            CreateSubmitRequest("{}"),
            _lambdaContext.Object);

        Assert.Equal(400, response.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    private static APIGatewayProxyRequest CreateSubmitRequest(string body) =>
        new()
        {
            PathParameters = new Dictionary<string, string>
            {
                ["groupReservationId"] = GroupReservationId
            },
            Body = body,
            RequestContext = new()
            {
                Authorizer = new APIGatewayCustomAuthorizerContext
                {
                    Claims = new Dictionary<string, string> { ["sub"] = OwnerId }
                }
            }
        };

    private static APIGatewayProxyRequest CreateRequest(
        string pathKey,
        string pathValue,
        bool includeAuthenticatedUser) =>
        new()
        {
            PathParameters = new Dictionary<string, string> { [pathKey] = pathValue },
            RequestContext = new()
            {
                Authorizer = new APIGatewayCustomAuthorizerContext
                {
                    Claims = includeAuthenticatedUser
                        ? new Dictionary<string, string> { ["sub"] = OwnerId }
                        : new Dictionary<string, string>()
                }
            }
        };
}
