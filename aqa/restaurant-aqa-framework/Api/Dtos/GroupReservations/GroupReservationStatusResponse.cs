namespace Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;

public sealed record GroupReservationStatusResponse(
    int CurrentParticipants,
    int MaxCapacity,
    string Status);
