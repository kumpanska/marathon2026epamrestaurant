using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Handlers;
using APIHandler.Services.Waiters;
using Moq;

namespace APIHandler.Tests;

public class DeleteWaiterHandlerTests
{
    [Fact]
    public async Task HandleRequest_EmailOnlyBody_RemovesWaiter()
    {
        var waiterService = new Mock<IWaiterService>();
        waiterService.Setup(service => service.IsWaiterInListAsync("waiter@example.com"))
            .ReturnsAsync(true);
        var handler = new DeleteWaiterHandler(waiterService.Object);
        var context = new Mock<ILambdaContext>();
        context.Setup(lambdaContext => lambdaContext.Logger).Returns(Mock.Of<ILambdaLogger>());
        var request = new APIGatewayProxyRequest
        {
            Body = """{"email":"Waiter@Example.com"}"""
        };

        var response = await handler.HandleRequest(request, context.Object);

        Assert.Equal(200, response.StatusCode);
        waiterService.Verify(service => service.RemoveWaiterFromListAsync("waiter@example.com"), Times.Once);
    }
}
