using Restaurant_Aqa_Framework.Api.Dtos.Reservations;

namespace Restaurant_Aqa_Framework.Screenplay;

public sealed record GroupReservationFixture(
    ReservationResponse Reservation,
    string GroupReservationId,
    string OwnerToken,
    string OwnerEmail,
    string OwnerPassword,
    string InviteToken);

public sealed record GroupGuestSession(string Id, string Name, string SessionToken);
