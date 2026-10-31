namespace Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;

public sealed record GroupParticipantsResponse(IReadOnlyList<GroupParticipant> Participants, decimal Total);

public sealed record GroupParticipant(
    string Id,
    string Type,
    string Name,
    IReadOnlyList<GroupOrderItem> Items,
    decimal Total,
    bool IsSubmitted);

public sealed record GroupOrderItem(GroupDish Dish, int Quantity);

public sealed record GroupDish(string Id, string Name, decimal Price);
