using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Repositories.GroupReservations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public class GuestSessionRepository(
    IDynamoDBContext context,
    IAmazonDynamoDB dynamoDbClient) : IGuestSessionRepository
{
    public async Task<GuestSession?> GetByGuestSessionId(string id) => await context.LoadAsync<GuestSession?>(id, DynamoConfig.LoadConfig(ResourceNames.GuestSessionTableName));

    public async Task<GuestSession?> GetByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<GuestSession?> GetByTokenHash(string tokenHash)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(tokenHash, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.SessionTokenHash
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId)
    {
        return await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();
    }

    public async Task<List<GuestSession>> GetAllByGroupReservationIdStronglyConsistentAsync(string groupReservationId)
    {
        var scan = context.ScanAsync<GuestSession>(
            [new ScanCondition(
                nameof(GuestSession.GroupReservationId),
                ScanOperator.Equal,
                groupReservationId)],
            new ScanConfig
            {
                OverrideTableName = ResourceNames.GuestSessionTableName,
                ConsistentRead = true,
            });

        return await scan.GetRemainingAsync();
    }

    public async Task<List<GuestSession>> GetOwnerSessionsByGroupReservationIdStronglyConsistentAsync(string groupReservationId)
    {
        var scan = context.ScanAsync<GuestSession>(
            [
                new ScanCondition(
                    nameof(GuestSession.GroupReservationId),
                    ScanOperator.Equal,
                    groupReservationId),
                new ScanCondition(
                    nameof(GuestSession.IsOwnerSession),
                    ScanOperator.Equal,
                    true),
            ],
            new ScanConfig
            {
                OverrideTableName = ResourceNames.GuestSessionTableName,
                ConsistentRead = true,
            });

        return await scan.GetRemainingAsync();
    }

    public async Task<bool> TryMarkSubmittedAsync(string guestSessionId, string groupReservationId)
    {
        try
        {
            await dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    CreateOpenGroupCondition(groupReservationId),
                    new TransactWriteItem
                    {
                        Update = new Update
                        {
                            TableName = ResourceNames.GuestSessionTableName,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["guestSessionId"] = new() { S = guestSessionId },
                            },
                            UpdateExpression = "SET #isSubmitted = :submitted",
                            ConditionExpression =
                                "attribute_exists(#guestSessionId) AND " +
                                "#groupReservationId = :groupReservationId AND " +
                                "(attribute_not_exists(#isSubmitted) OR #isSubmitted = :notSubmitted)",
                            ExpressionAttributeNames = new Dictionary<string, string>
                            {
                                ["#guestSessionId"] = "guestSessionId",
                                ["#groupReservationId"] = "groupReservationId",
                                ["#isSubmitted"] = "isSubmitted",
                            },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":submitted"] = new() { BOOL = true },
                                [":groupReservationId"] = new() { S = groupReservationId },
                                [":notSubmitted"] = new() { BOOL = false },
                            },
                        },
                    },
                ],
            });
            return true;
        }
        catch (TransactionCanceledException)
        {
            var group = await dynamoDbClient.GetItemAsync(new GetItemRequest
            {
                TableName = ResourceNames.GroupReservationsTableName,
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
            var session = await GetSessionStatusAsync(guestSessionId);

            bool groupIsOpen = group.Item is not null &&
                group.Item.TryGetValue("status", out AttributeValue? status) &&
                DynamoGroupReservationStatus.Is(status, GroupReservationStatus.Open);
            bool sessionAlreadySubmitted =
                session.Item is not null &&
                session.Item.TryGetValue("isSubmitted", out AttributeValue? isSubmitted) &&
                isSubmitted is not null &&
                isSubmitted.BOOL == true;
            bool sessionExists = session.Item?.ContainsKey("guestSessionId") == true;

            if (!groupIsOpen || !sessionExists || sessionAlreadySubmitted)
                return false;

            // A transaction conflict or another cancellation reason occurred while
            // both conditional preconditions still hold; surface it for retry.
            throw;
        }
    }

    public async Task<bool> TryUpdateDishesWhileGroupOpenAsync(GuestSession guestSession)
    {
        try
        {
            await dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    CreateOpenGroupCondition(guestSession.GroupReservationId),
                    new TransactWriteItem
                    {
                        Update = new Update
                        {
                            TableName = ResourceNames.GuestSessionTableName,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["guestSessionId"] = new() { S = guestSession.Id },
                            },
                            UpdateExpression = "SET #dishes = :dishes",
                            ConditionExpression =
                                "attribute_exists(#guestSessionId) AND " +
                                "#groupReservationId = :groupReservationId AND " +
                                "(attribute_not_exists(#isSubmitted) OR #isSubmitted = :notSubmitted)",
                            ExpressionAttributeNames = new Dictionary<string, string>
                            {
                                ["#guestSessionId"] = "guestSessionId",
                                ["#groupReservationId"] = "groupReservationId",
                                ["#dishes"] = "dishes",
                                ["#isSubmitted"] = "isSubmitted",
                            },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":dishes"] = GetDishesAttributeValue(guestSession),
                                [":groupReservationId"] = new() { S = guestSession.GroupReservationId },
                                [":notSubmitted"] = new() { BOOL = false },
                            },
                        },
                    },
                ],
            });
            return true;
        }
        catch (TransactionCanceledException)
        {
            if (!await IsGroupOpenAsync(guestSession.GroupReservationId))
                return false;

            var session = await GetSessionStatusAsync(guestSession.Id);
            if (session.Item is null || !session.Item.ContainsKey("guestSessionId"))
                return false;

            if (session.Item.TryGetValue("isSubmitted", out AttributeValue? isSubmitted) &&
                isSubmitted is not null &&
                isSubmitted.BOOL == true)
            {
                return false;
            }

            throw;
        }
    }

    public async Task<bool> TryDeleteWhileGroupOpenAsync(string guestSessionId, string groupReservationId)
    {
        try
        {
            await dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    CreateOpenGroupCondition(groupReservationId),
                    new TransactWriteItem
                    {
                        Delete = new Delete
                        {
                            TableName = ResourceNames.GuestSessionTableName,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["guestSessionId"] = new() { S = guestSessionId },
                            },
                            ConditionExpression =
                                "attribute_exists(#guestSessionId) AND #groupReservationId = :groupReservationId",
                            ExpressionAttributeNames = new Dictionary<string, string>
                            {
                                ["#guestSessionId"] = "guestSessionId",
                                ["#groupReservationId"] = "groupReservationId",
                            },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":groupReservationId"] = new() { S = groupReservationId },
                            },
                        },
                    },
                ],
            });
            return true;
        }
        catch (TransactionCanceledException)
        {
            if (!await IsGroupOpenAsync(groupReservationId))
                return false;

            var session = await GetSessionStatusAsync(guestSessionId);
            if (session.Item is null || !session.Item.ContainsKey("guestSessionId"))
                return false;

            throw;
        }
    }

    private static TransactWriteItem CreateOpenGroupCondition(string groupReservationId) =>
        new()
        {
            ConditionCheck = new ConditionCheck
            {
                TableName = ResourceNames.GroupReservationsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["groupReservationId"] = new() { S = groupReservationId },
                },
                ConditionExpression =
                    "#status = :openName OR #status = :openStringNumber OR #status = :openNumber",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = "status",
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":openName"] = new() { S = GroupReservationStatus.Open.ToString() },
                    [":openStringNumber"] = new() { S = ((int)GroupReservationStatus.Open).ToString() },
                    [":openNumber"] = new() { N = ((int)GroupReservationStatus.Open).ToString() },
                },
            },
        };

    private async Task<bool> IsGroupOpenAsync(string groupReservationId)
    {
        var group = await dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = ResourceNames.GroupReservationsTableName,
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

        return group.Item is not null &&
            group.Item.TryGetValue("status", out AttributeValue? status) &&
            DynamoGroupReservationStatus.Is(status, GroupReservationStatus.Open);
    }

    private async Task<GetItemResponse> GetSessionStatusAsync(string guestSessionId) =>
        await dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = ResourceNames.GuestSessionTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["guestSessionId"] = new() { S = guestSessionId },
            },
            ConsistentRead = true,
            ProjectionExpression = "#guestSessionId, #isSubmitted",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#guestSessionId"] = "guestSessionId",
                ["#isSubmitted"] = "isSubmitted",
            },
        });

    private static AttributeValue GetDishesAttributeValue(GuestSession guestSession) => new()
    {
        L = guestSession.Dishes.Select(dish => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["dishId"] = new() { S = dish.DishId },
                ["quantity"] = new()
                {
                    N = dish.Quantity.ToString(CultureInfo.InvariantCulture),
                },
                ["state"] = new()
                {
                    N = ((int)dish.State).ToString(CultureInfo.InvariantCulture),
                },
            },
        }).ToList(),
    };

    public async Task SaveAsync(GuestSession guestSession) => await context.SaveAsync(guestSession, new SaveConfig
    {
        OverrideTableName = ResourceNames.GuestSessionTableName
    });

    public async Task<int> CountByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.Count(session => !session.IsOwnerSession);
    }

    public async Task DeleteAsync(GuestSession session)
    {
        if (!await TryDeleteWhileGroupOpenAsync(session.Id, session.GroupReservationId))
        {
            throw new InvalidOperationException(
                $"Guest session {session.Id} could not be deleted because its group is no longer open or the session no longer exists.");
        }
    }
}
