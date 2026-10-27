using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.GroupReservations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Orders;

public class OrderRepository : IOrderRepository
{
    private readonly string _ordersTableName = ResourceNames.OrdersTableName;
    private readonly string _customerIdGlobalIndex = ResourceNames.OrdersTableCustomerIdGlobalIndex;
    private readonly string _reservationIdGlobalIndex = ResourceNames.OrdersTableReservationIdGlobalIndex;
    private readonly string _groupReservationsTableName = ResourceNames.GroupReservationsTableName;

    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public OrderRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<Order?> GetOrderByIdAsync(string orderId, string customerId)
    {
        return await _dynamoDbContext.LoadAsync<Order>(orderId, customerId, DynamoConfig.LoadConfig(_ordersTableName));
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = _ordersTableName,
            IndexName = _customerIdGlobalIndex,
        };

        var query = _dynamoDbContext.QueryAsync<Order>(customerId, config);

        return await query.GetRemainingAsync();
    }

    public async Task<Order?> GetOrderByReservationIdAsync(string reservationId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = _ordersTableName,
            IndexName = _reservationIdGlobalIndex,
        };

        var results = await _dynamoDbContext.QueryAsync<Order>(reservationId, config).GetNextSetAsync();

        return results.FirstOrDefault(o => o.Status != OrderStatus.Cancelled);
    }

    public async Task SaveOrderAsync(Order order)
    {
        long nextVersion = (order.ConcurrencyVersion ?? 0) + 1;
        var expressionAttributeNames = new Dictionary<string, string>
        {
            ["#reservationId"] = "reservationId",
            ["#reservationAddress"] = "reservationAddress",
            ["#reservationTime"] = "reservationTime",
            ["#dishes"] = "dishes",
            ["#status"] = "status",
            ["#version"] = "concurrencyVersion",
        };
        var expressionAttributeValues = GetOrderAttributeValues(order);
        expressionAttributeValues[":version"] = new AttributeValue
        {
            N = nextVersion.ToString(CultureInfo.InvariantCulture),
        };

        string versionCondition = order.ConcurrencyVersion.HasValue
            ? "#version = :expectedVersion"
            : "attribute_not_exists(#version)";
        if (order.ConcurrencyVersion.HasValue)
        {
            expressionAttributeValues[":expectedVersion"] = new AttributeValue
            {
                N = order.ConcurrencyVersion.Value.ToString(CultureInfo.InvariantCulture),
            };
        }

        try
        {
            await _dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _ordersTableName,
                Key = GetOrderKey(order),
                UpdateExpression =
                    "SET #reservationId = :reservationId, #reservationAddress = :reservationAddress, " +
                    "#reservationTime = :reservationTime, #dishes = :dishes, #status = :orderStatus, " +
                    "#version = :version",
                ConditionExpression = versionCondition,
                ExpressionAttributeNames = expressionAttributeNames,
                ExpressionAttributeValues = expressionAttributeValues,
            });
            order.ConcurrencyVersion = nextVersion;
        }
        catch (ConditionalCheckFailedException exception)
        {
            throw new OrderConcurrencyException(
                $"Order {order.OrderId} changed while it was being saved.",
                exception);
        }
    }

    public async Task<bool> TrySaveOrderAndSubmitGroupReservationAsync(Order order, string groupReservationId)
    {
        var currentGroupReservation = await _dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = _groupReservationsTableName,
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

        if (currentGroupReservation.Item is null ||
            !currentGroupReservation.Item.TryGetValue("status", out AttributeValue? currentStatus))
        {
            return false;
        }

        if (!DynamoGroupReservationStatus.Is(currentStatus, GroupReservationStatus.Submitting))
            return false;

        AttributeValue submittedStatus =
            DynamoGroupReservationStatus.Convert(currentStatus!, GroupReservationStatus.Submitted);

        long nextVersion = (order.ConcurrencyVersion ?? 0) + 1;
        var expressionAttributeNames = new Dictionary<string, string>
        {
            ["#dishes"] = "dishes",
            ["#status"] = "status",
            ["#orderId"] = "orderId",
            ["#customerId"] = "customerId",
            ["#version"] = "concurrencyVersion",
        };
        var expressionAttributeValues = new Dictionary<string, AttributeValue>
        {
            [":dishes"] = GetOrderDishesAttributeValue(order),
            [":orderStatus"] = new()
            {
                N = ((int)order.Status).ToString(CultureInfo.InvariantCulture),
            },
        };
        expressionAttributeValues[":version"] = new AttributeValue
        {
            N = nextVersion.ToString(CultureInfo.InvariantCulture),
        };

        string orderCondition = "attribute_exists(#orderId) AND attribute_exists(#customerId)";
        if (order.ConcurrencyVersion.HasValue)
        {
            orderCondition += " AND #version = :expectedVersion";
            expressionAttributeValues[":expectedVersion"] = new AttributeValue
            {
                N = order.ConcurrencyVersion.Value.ToString(CultureInfo.InvariantCulture),
            };
        }
        else
        {
            orderCondition += " AND attribute_not_exists(#version)";
        }

        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = _groupReservationsTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["groupReservationId"] = new() { S = groupReservationId },
                        },
                        UpdateExpression = "SET #status = :submitted",
                        ConditionExpression = "#status = :submitting",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#status"] = "status",
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":submitting"] = currentStatus!,
                            [":submitted"] = submittedStatus,
                        },
                    },
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = _ordersTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["orderId"] = new() { S = order.OrderId },
                            ["customerId"] = new() { S = order.CustomerId },
                        },
                        UpdateExpression = "SET #dishes = :dishes, #status = :orderStatus, #version = :version",
                        ConditionExpression = orderCondition,
                        ExpressionAttributeNames = expressionAttributeNames,
                        ExpressionAttributeValues = expressionAttributeValues,
                    },
                },
            ],
        };

        try
        {
            await _dynamoDbClient.TransactWriteItemsAsync(request);
            order.ConcurrencyVersion = nextVersion;
            return true;
        }
        catch (TransactionCanceledException)
        {
            // Re-read group state first to distinguish a competing finalizer from
            // an optimistic order-version conflict.
            var groupReservation = await _dynamoDbClient.GetItemAsync(new GetItemRequest
            {
                TableName = _groupReservationsTableName,
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

            if (groupReservation.Item is null ||
                !groupReservation.Item.TryGetValue("status", out AttributeValue? status) ||
            !DynamoGroupReservationStatus.Is(status, GroupReservationStatus.Submitting))
            {
                return false;
            }

            if (await OrderVersionChangedAsync(order))
            {
                throw new OrderConcurrencyException(
                    $"Order {order.OrderId} changed while the group reservation was being submitted.");
            }

            throw;
        }
    }

    private async Task<bool> OrderVersionChangedAsync(Order order)
    {
        var currentOrder = await _dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = _ordersTableName,
            Key = GetOrderKey(order),
            ConsistentRead = true,
            ProjectionExpression = "#version",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#version"] = "concurrencyVersion",
            },
        });

        if (currentOrder.Item is null)
            return false;

        bool hasCurrentVersion = currentOrder.Item.TryGetValue(
            "concurrencyVersion",
            out AttributeValue? currentVersion);
        if (!order.ConcurrencyVersion.HasValue)
            return hasCurrentVersion;

        return !hasCurrentVersion ||
            !long.TryParse(currentVersion.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out long version) ||
            version != order.ConcurrencyVersion.Value;
    }

    private static Dictionary<string, AttributeValue> GetOrderKey(Order order) => new()
    {
        ["orderId"] = new() { S = order.OrderId },
        ["customerId"] = new() { S = order.CustomerId },
    };

    private static Dictionary<string, AttributeValue> GetOrderAttributeValues(Order order) => new()
    {
        [":reservationId"] = new() { S = order.ReservationId },
        [":reservationAddress"] = new() { S = order.ReservationAddress },
        [":reservationTime"] = new() { S = order.ReservationTime },
        [":dishes"] = GetOrderDishesAttributeValue(order),
        [":orderStatus"] = new()
        {
            N = ((int)order.Status).ToString(CultureInfo.InvariantCulture),
        },
    };

    private static AttributeValue GetOrderDishesAttributeValue(Order order) => new()
    {
        L = order.Dishes.Select(dish => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["dishId"] = new() { S = dish.DishId },
                ["quantity"] = new() { N = dish.Quantity.ToString(CultureInfo.InvariantCulture) },
                ["state"] = new() { N = ((int)dish.State).ToString(CultureInfo.InvariantCulture) },
            },
        }).ToList(),
    };

}
