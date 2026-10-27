using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Repositories.GroupReservations;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace APIHandler.Tests.Repositories;

[Collection("Order repository environment")]
public class GroupReservationRepositoryTests : IDisposable
{
    private const string GroupReservationsTable = "group-reservations-test";
    private const string GroupReservationId = "group-123";

    private readonly Mock<IDynamoDBContext> _context = new();
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly GroupReservationRepository _repository;
    private readonly string? _previousGroupReservationsTable =
        Environment.GetEnvironmentVariable("groupReservationsTable");

    public GroupReservationRepositoryTests()
    {
        Environment.SetEnvironmentVariable("groupReservationsTable", GroupReservationsTable);
        _repository = new GroupReservationRepository(_context.Object, _dynamoDb.Object);
    }

    [Fact]
    public async Task TryBeginSubmissionAsync_TransitionsOnlyAnOpenGroupToSubmitting()
    {
        GetItemRequest? capturedRead = null;
        UpdateItemRequest? capturedUpdate = null;
        _dynamoDb
            .Setup(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>((request, _) => capturedRead = request)
            .ReturnsAsync(GroupStatusResponse("Open"));
        _dynamoDb
            .Setup(client => client.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((request, _) => capturedUpdate = request)
            .ReturnsAsync(new UpdateItemResponse());

        bool claimed = await _repository.TryBeginSubmissionAsync(GroupReservationId);

        Assert.True(claimed);
        Assert.NotNull(capturedRead);
        Assert.Equal(GroupReservationsTable, capturedRead.TableName);
        Assert.True(capturedRead.ConsistentRead);
        Assert.Equal(GroupReservationId, capturedRead.Key["groupReservationId"].S);
        Assert.NotNull(capturedUpdate);
        Assert.Equal("#status = :expectedStatus", capturedUpdate.ConditionExpression);
        Assert.Equal("Open", capturedUpdate.ExpressionAttributeValues[":expectedStatus"].S);
        Assert.Equal("Submitting", capturedUpdate.ExpressionAttributeValues[":nextStatus"].S);
        Assert.Equal("SET #status = :nextStatus", capturedUpdate.UpdateExpression);
    }

    [Fact]
    public async Task TryBeginSubmissionAsync_WhenGroupIsNotOpen_DoesNotUpdateStatus()
    {
        _dynamoDb
            .Setup(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GroupStatusResponse("Submitting"));

        bool claimed = await _repository.TryBeginSubmissionAsync(GroupReservationId);

        Assert.False(claimed);
        _dynamoDb.Verify(
            client => client.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryRestoreOpenAfterFailedSubmissionAsync_TransitionsSubmittingGroupBackToOpen()
    {
        UpdateItemRequest? capturedUpdate = null;
        _dynamoDb
            .Setup(client => client.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GroupStatusResponse("Submitting"));
        _dynamoDb
            .Setup(client => client.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((request, _) => capturedUpdate = request)
            .ReturnsAsync(new UpdateItemResponse());

        bool restored = await _repository.TryRestoreOpenAfterFailedSubmissionAsync(GroupReservationId);

        Assert.True(restored);
        Assert.NotNull(capturedUpdate);
        Assert.Equal("#status = :expectedStatus", capturedUpdate.ConditionExpression);
        Assert.Equal("Submitting", capturedUpdate.ExpressionAttributeValues[":expectedStatus"].S);
        Assert.Equal("Open", capturedUpdate.ExpressionAttributeValues[":nextStatus"].S);
    }

    private static GetItemResponse GroupStatusResponse(string status) =>
        new()
        {
            Item = new Dictionary<string, AttributeValue>
            {
                ["status"] = new() { S = status }
            }
        };

    public void Dispose() =>
        Environment.SetEnvironmentVariable("groupReservationsTable", _previousGroupReservationsTable);
}
