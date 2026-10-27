using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.Dishes;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests;

public class DeleteGuestSessionHandlerTests
{
    private const string GroupReservationId = "group-123";
    private const string GuestSessionId = "session-123";
    private const string OwnerId = "owner-123";

    [Fact]
    public async Task HandleRequest_SessionWasDeleted_ReturnsUpdatedSnapshot()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId))
            .ReturnsAsync(new GetAllSessionsResponse(
            [
                new Participant(
                    "remaining-session",
                    ParticipantType.Guest,
                    "Sam",
                    [
                        new OrderItemResponse
                        {
                            Dish = new DishResponse
                            {
                                Id = "dish-1",
                                Name = "Pasta",
                                Description = "Fresh pasta",
                                DishType = "Main course",
                                Price = 8.50m,
                                Weight = "300g",
                                Calories = "500",
                                Proteins = "20g",
                                Fats = "15g",
                                Carbohydrates = "40g",
                                Vitamins = "B",
                                ImageUrl = null,
                                State = "Available"
                            },
                            Quantity = 2
                        }
                    ],
                    17.00m,
                    false)
            ],
            17.00m));

        var response = await CreateHandler(guestSessionService).HandleRequest(
            CreateRequest(),
            CreateContext());

        Assert.Equal(200, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(response.Body);
        JsonElement participant = Assert.Single(body.RootElement.GetProperty("participants").EnumerateArray());
        Assert.Equal("remaining-session", participant.GetProperty("id").GetString());
        Assert.Equal("Guest", participant.GetProperty("type").GetString());
        Assert.Equal("Sam", participant.GetProperty("name").GetString());
        Assert.False(participant.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(17.00m, participant.GetProperty("total").GetDecimal());
        JsonElement item = Assert.Single(participant.GetProperty("items").EnumerateArray());
        Assert.Equal("dish-1", item.GetProperty("dish").GetProperty("id").GetString());
        Assert.Equal("Pasta", item.GetProperty("dish").GetProperty("name").GetString());
        Assert.Equal(8.50m, item.GetProperty("dish").GetProperty("price").GetDecimal());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(17.00m, body.RootElement.GetProperty("total").GetDecimal());
        guestSessionService.Verify(
            service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId),
            Times.Once);
    }

    [Fact]
    public async Task HandleRequest_SessionDoesNotExist_ReturnsNotFound()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId))
            .ThrowsAsync(new GuestSessionNotFoundException("session not found"));

        var response = await CreateHandler(guestSessionService).HandleRequest(
            CreateRequest(),
            CreateContext());

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_SessionBelongsToAnotherGroup_ReturnsBadRequest()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId))
            .ThrowsAsync(new ValidationException("Guest session does not belong to this group reservation."));

        var response = await CreateHandler(guestSessionService).HandleRequest(
            CreateRequest(),
            CreateContext());

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_GroupIsClosed_ReturnsConflict()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId))
            .ThrowsAsync(new GroupReservationExpiredException("group is closed"));

        var response = await CreateHandler(guestSessionService).HandleRequest(
            CreateRequest(),
            CreateContext());

        Assert.Equal(409, response.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_OwnerSessionCannotBeDeleted_ReturnsForbidden()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId))
            .ThrowsAsync(new ActionForbiddenException("Owner sessions cannot be deleted."));

        var response = await CreateHandler(guestSessionService).HandleRequest(
            CreateRequest(),
            CreateContext());

        Assert.Equal(403, response.StatusCode);
        guestSessionService.Verify(
            service => service.DeleteGuestSessionAsync(OwnerId, GroupReservationId, GuestSessionId),
            Times.Once);
    }

    private static DeleteGuestSessionHandler CreateHandler(Mock<IGuestSessionService> guestSessionService)
        => new(guestSessionService.Object);

    private static APIGatewayProxyRequest CreateRequest() => new()
    {
        PathParameters = new Dictionary<string, string>
        {
            ["groupReservationId"] = GroupReservationId,
            ["guestSessionId"] = GuestSessionId
        },
        RequestContext = new()
        {
            Authorizer = new APIGatewayCustomAuthorizerContext
            {
                Claims = new Dictionary<string, string> { ["sub"] = OwnerId }
            }
        }
    };

    private static ILambdaContext CreateContext()
    {
        var context = new Mock<ILambdaContext>();
        context.Setup(lambdaContext => lambdaContext.Logger).Returns(Mock.Of<ILambdaLogger>());
        return context.Object;
    }
}
