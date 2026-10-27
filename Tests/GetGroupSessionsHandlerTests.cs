using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests;

public class GetGroupSessionsHandlerTests
{
    [Fact]
    public async Task HandleRequest_MissingUserClaims_ReturnsUnauthorizedWithoutCallingService()
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        var handler = new GetGroupSessionsHandler(guestSessionService.Object);
        var lambdaContext = new Mock<ILambdaContext>();
        var logger = new Mock<ILambdaLogger>();
        lambdaContext.Setup(context => context.Logger).Returns(logger.Object);
        var request = new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["groupReservationId"] = "group-123"
            }
        };

        var response = await handler.HandleRequest(request, lambdaContext.Object);

        Assert.Equal(401, response.StatusCode);
        guestSessionService.Verify(
            service => service.GetGroupSessionsAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
