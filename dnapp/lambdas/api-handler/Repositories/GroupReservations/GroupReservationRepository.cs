using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupReservations;

public class GroupReservationRepository : IGroupReservationRepository
{
    private static readonly string GroupReservationsTableName = ResourceNames.GroupReservationsTableName;
    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public GroupReservationRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<GroupReservation?> GetGroupReservationByIdAsync(string groupReservationId)
    {
        return await _dynamoDbContext.LoadAsync<GroupReservation>(groupReservationId, DynamoConfig.LoadConfig(GroupReservationsTableName));
    }

    public async Task DeleteGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.DeleteAsync(groupReservation, DynamoConfig.DeleteConfig(GroupReservationsTableName));
    }

    public async Task SaveGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.SaveAsync(groupReservation, DynamoConfig.SaveConfig(GroupReservationsTableName));
    }

    public async Task<GroupReservation?> GetGroupReservationByTokenAsync(string token)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(token, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationToken,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }

    public async Task<GroupReservation?> GetByReservationIdAsync(string reservationId)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(reservationId, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationId,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }

    public async Task SetOwnerGuestSessionIdAsync(string groupReservationId, string ownerGuestSessionId)
    {
        await _dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = GroupReservationsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["groupReservationId"] = new() { S = groupReservationId },
            },
            UpdateExpression = "SET #ownerGuestSessionId = :ownerGuestSessionId",
            ConditionExpression = "attribute_exists(#groupReservationId)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#groupReservationId"] = "groupReservationId",
                ["#ownerGuestSessionId"] = "ownerGuestSessionId",
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ownerGuestSessionId"] = new() { S = ownerGuestSessionId },
            },
        });
    }

    public Task<bool> TryBeginSubmissionAsync(string groupReservationId) =>
        TryTransitionStatusAsync(groupReservationId, GroupReservationStatus.Open, GroupReservationStatus.Submitting);

    public Task<bool> TryRestoreOpenAfterFailedSubmissionAsync(string groupReservationId) =>
        TryTransitionStatusAsync(groupReservationId, GroupReservationStatus.Submitting, GroupReservationStatus.Open);

    private async Task<bool> TryTransitionStatusAsync(
        string groupReservationId,
        GroupReservationStatus expectedStatus,
        GroupReservationStatus nextStatus)
    {
        var currentGroup = await _dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = GroupReservationsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["groupReservationId"] = new() { S = groupReservationId },
            },
            ConsistentRead = true,
            ProjectionExpression = "#status",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "status",
            },
        });

        if (currentGroup.Item is null ||
            !currentGroup.Item.TryGetValue("status", out AttributeValue? storedStatus) ||
            !DynamoGroupReservationStatus.Is(storedStatus, expectedStatus))
        {
            return false;
        }

        AttributeValue nextStoredStatus = DynamoGroupReservationStatus.Convert(storedStatus!, nextStatus);
        try
        {
            await _dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = GroupReservationsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["groupReservationId"] = new() { S = groupReservationId },
                },
                UpdateExpression = "SET #status = :nextStatus",
                ConditionExpression = "#status = :expectedStatus",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = "status",
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedStatus"] = storedStatus,
                    [":nextStatus"] = nextStoredStatus,
                },
            });
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}
