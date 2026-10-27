using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Repositories.Orders;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace APIHandler.Tests.Repositories;

[CollectionDefinition("Order repository environment", DisableParallelization = true)]
public sealed class OrderRepositoryTestCollection
{
}

[Collection("Order repository environment")]
public class OrderRepositoryTests : IDisposable
{
    private const string OrdersTable = "orders-test";
    private const string GroupReservationsTable = "group-reservations-test";
    private const string GroupReservationId = "group-123";

    private readonly Mock<IDynamoDBContext> _context = new();
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly OrderRepository _repository;
    private readonly string? _previousOrdersTable = Environment.GetEnvironmentVariable("ordersTable");
    private readonly string? _previousGroupReservationsTable =
        Environment.GetEnvironmentVariable("groupReservationsTable");

    public OrderRepositoryTests()
    {
        Environment.SetEnvironmentVariable("ordersTable", OrdersTable);
        Environment.SetEnvironmentVariable("groupReservationsTable", GroupReservationsTable);
        _repository = new OrderRepository(_context.Object, _dynamoDb.Object);
    }

    [Fact]
    public async Task TrySaveOrderAndSubmitGroupReservationAsync_WritesOrderAndGroupTransitionInOneConditionalTransaction()
    {
        TransactWriteItemsRequest? capturedRequest = null;
        SetupGroupStatus("Submitting");
        _dynamoDb
            .Setup(client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new TransactWriteItemsResponse());

        Order order = CreateOrder();
        order.ConcurrencyVersion = 7;
        bool submitted = await _repository.TrySaveOrderAndSubmitGroupReservationAsync(order, GroupReservationId);

        Assert.True(submitted);
        Assert.Equal(8L, order.ConcurrencyVersion);
        Assert.NotNull(capturedRequest);
        Assert.Equal(2, capturedRequest.TransactItems.Count);

        Update groupTransition = Assert.IsType<Update>(capturedRequest.TransactItems[0].Update);
        Assert.Equal(GroupReservationsTable, groupTransition.TableName);
        Assert.Equal(GroupReservationId, groupTransition.Key["groupReservationId"].S);
        Assert.Equal("#status = :submitting", groupTransition.ConditionExpression);
        Assert.Contains(":submitting", groupTransition.ExpressionAttributeValues.Keys);
        Assert.Equal("Submitting", groupTransition.ExpressionAttributeValues[":submitting"].S);
        Assert.Equal("Submitted", groupTransition.ExpressionAttributeValues[":submitted"].S);
        Assert.Equal("SET #status = :submitted", groupTransition.UpdateExpression);

        Update orderUpdate = Assert.IsType<Update>(capturedRequest.TransactItems[1].Update);
        Assert.Equal(OrdersTable, orderUpdate.TableName);
        Assert.Equal("order-123", orderUpdate.Key["orderId"].S);
        Assert.Equal("owner-123", orderUpdate.Key["customerId"].S);
        Assert.Equal(
            "attribute_exists(#orderId) AND attribute_exists(#customerId) AND #version = :expectedVersion",
            orderUpdate.ConditionExpression);
        Assert.Equal("SET #dishes = :dishes, #status = :orderStatus, #version = :version", orderUpdate.UpdateExpression);
        Assert.Equal("7", orderUpdate.ExpressionAttributeValues[":expectedVersion"].N);
        Assert.Equal("8", orderUpdate.ExpressionAttributeValues[":version"].N);
        Assert.Equal(((int)OrderStatus.SubmittedByCustomer).ToString(), orderUpdate.ExpressionAttributeValues[":orderStatus"].N);

        List<AttributeValue> storedDishes = orderUpdate.ExpressionAttributeValues[":dishes"].L;
        Assert.Collection(
            storedDishes,
            dish =>
            {
                Assert.Equal("dish-1", dish.M["dishId"].S);
                Assert.Equal("5", dish.M["quantity"].N);
                Assert.Equal(((int)OrderItemState.Pending).ToString(), dish.M["state"].N);
            },
            dish =>
            {
                Assert.Equal("dish-2", dish.M["dishId"].S);
                Assert.Equal("2", dish.M["quantity"].N);
                Assert.Equal(((int)OrderItemState.Confirmed).ToString(), dish.M["state"].N);
            });
        _dynamoDb.Verify(
            client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TrySaveOrderAndSubmitGroupReservationAsync_WhenTransactionLosesSubmissionRace_DoesNotRetryOrWriteQuantitiesAgain()
    {
        SetupGroupStatus("Submitting");
        _dynamoDb
            .SetupSequence(client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("Conditional group transition failed."));
        _dynamoDb
            .SetupSequence(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GroupStatusResponse("Submitting"))
            .ReturnsAsync(GroupStatusResponse("Submitted"));

        bool submitted = await _repository.TrySaveOrderAndSubmitGroupReservationAsync(CreateOrder(), GroupReservationId);

        Assert.False(submitted);
        _dynamoDb.Verify(
            client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _dynamoDb.Verify(
            client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TrySaveOrderAndSubmitGroupReservationAsync_WhenAlreadySubmitted_DoesNotStartAnotherTransaction()
    {
        SetupGroupStatus("Submitting");
        _dynamoDb
            .Setup(client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactWriteItemsResponse());
        Order order = CreateOrder();

        Assert.True(await _repository.TrySaveOrderAndSubmitGroupReservationAsync(order, GroupReservationId));

        SetupGroupStatus("Submitted");
        Assert.False(await _repository.TrySaveOrderAndSubmitGroupReservationAsync(order, GroupReservationId));

        _dynamoDb.Verify(
            client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupGroupStatus(string status)
    {
        _dynamoDb
            .Setup(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GroupStatusResponse(status));
    }

    private static GetItemResponse GroupStatusResponse(string status) =>
        new()
        {
            Item = new Dictionary<string, AttributeValue>
            {
                ["status"] = new() { S = status }
            }
        };

    private static Order CreateOrder() =>
        new()
        {
            OrderId = "order-123",
            CustomerId = "owner-123",
            ReservationId = "reservation-123",
            ReservationAddress = "Address",
            ReservationTime = "Time",
            Status = OrderStatus.SubmittedByCustomer,
            Dishes =
            [
                new OrderItem { DishId = "dish-1", Quantity = 5, State = OrderItemState.Pending },
                new OrderItem { DishId = "dish-2", Quantity = 2, State = OrderItemState.Confirmed }
            ]
        };

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ordersTable", _previousOrdersTable);
        Environment.SetEnvironmentVariable("groupReservationsTable", _previousGroupReservationsTable);
    }
}
