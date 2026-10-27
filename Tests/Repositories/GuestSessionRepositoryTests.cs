using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using APIHandler.Repositories.GuestSessions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace APIHandler.Tests.Repositories;

[Collection("Order repository environment")]
public class GuestSessionRepositoryTests : IDisposable
{
    private const string GroupReservationsTable = "group-reservations-test";
    private const string GuestSessionTable = "guest-sessions-test";
    private const string GroupReservationId = "group-123";
    private const string GuestSessionId = "guest-session-123";

    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly GuestSessionRepository _repository;
    private readonly string? _previousGroupReservationsTable =
        Environment.GetEnvironmentVariable("groupReservationsTable");
    private readonly string? _previousGuestSessionTable =
        Environment.GetEnvironmentVariable("guestSessionTable");

    public GuestSessionRepositoryTests()
    {
        Environment.SetEnvironmentVariable("groupReservationsTable", GroupReservationsTable);
        Environment.SetEnvironmentVariable("guestSessionTable", GuestSessionTable);
        _repository = new GuestSessionRepository(Mock.Of<Amazon.DynamoDBv2.DataModel.IDynamoDBContext>(), _dynamoDb.Object);
    }

    [Fact]
    public async Task TryMarkSubmittedAsync_WhenGroupIsSubmitting_RejectsGuestSubmission()
    {
        TransactWriteItemsRequest? capturedTransaction = null;
        var readRequests = new List<GetItemRequest>();
        _dynamoDb
            .Setup(client => client.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => capturedTransaction = request)
            .ThrowsAsync(new TransactionCanceledException("The group is no longer open."));
        _dynamoDb
            .Setup(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>((request, _) => readRequests.Add(request))
            .ReturnsAsync((GetItemRequest request, CancellationToken _) =>
                request.TableName == GroupReservationsTable
                    ? new GetItemResponse
                    {
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["status"] = new() { S = "Submitting" }
                        }
                    }
                    : new GetItemResponse
                    {
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["guestSessionId"] = new() { S = GuestSessionId },
                            ["isSubmitted"] = new() { BOOL = false }
                        }
                    });

        bool submitted = await _repository.TryMarkSubmittedAsync(GuestSessionId, GroupReservationId);

        Assert.False(submitted);
        Assert.NotNull(capturedTransaction);
        Assert.Equal(2, capturedTransaction.TransactItems.Count);
        Assert.NotNull(capturedTransaction.TransactItems[0].ConditionCheck);
        Assert.Contains(
            "#status = :openName",
            capturedTransaction.TransactItems[0].ConditionCheck.ConditionExpression);
        Assert.Equal(2, readRequests.Count);
        Assert.All(readRequests, request => Assert.True(request.ConsistentRead));
        Assert.Equal(GroupReservationsTable, readRequests[0].TableName);
        Assert.Equal(GuestSessionTable, readRequests[1].TableName);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("groupReservationsTable", _previousGroupReservationsTable);
        Environment.SetEnvironmentVariable("guestSessionTable", _previousGuestSessionTable);
    }
}
