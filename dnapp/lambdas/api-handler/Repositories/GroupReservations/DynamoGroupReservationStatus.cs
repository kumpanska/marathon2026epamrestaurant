using Amazon.DynamoDBv2.Model;
using APIHandler.Entities;
using System;
using System.Globalization;

namespace APIHandler.Repositories.GroupReservations;

internal static class DynamoGroupReservationStatus
{
    public static bool Is(AttributeValue? value, GroupReservationStatus status)
    {
        if (value is null)
            return false;

        string numericStatus = ((int)status).ToString(CultureInfo.InvariantCulture);
        return value.S == status.ToString() ||
            value.S == numericStatus ||
            value.N == numericStatus;
    }

    public static AttributeValue Convert(AttributeValue storedValue, GroupReservationStatus status)
    {
        if (storedValue.S is not null)
        {
            string value = int.TryParse(storedValue.S, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? ((int)status).ToString(CultureInfo.InvariantCulture)
                : status.ToString();
            return new AttributeValue { S = value };
        }

        if (storedValue.N is not null)
        {
            return new AttributeValue
            {
                N = ((int)status).ToString(CultureInfo.InvariantCulture),
            };
        }

        throw new InvalidOperationException("Group reservation status has an unsupported DynamoDB representation.");
    }
}
