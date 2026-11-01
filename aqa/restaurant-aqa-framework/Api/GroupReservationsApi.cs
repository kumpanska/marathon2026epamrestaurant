using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;

namespace Restaurant_Aqa_Framework.Api;

public sealed class GroupReservationsApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetInviteTokenAsync(string reservationId, string? bearerToken = null) =>
        GetAsync(Url($"/reservations/{reservationId}/invite-token"),
            new APIRequestContextOptions { Headers = Headers(bearerToken) });

    public Task<IAPIResponse> GetGroupFromInviteAsync(string inviteToken) =>
        GetAsync(Url($"/guest/invites/{inviteToken}"));

    public Task<IAPIResponse> GetOwnerGuestSessionAsync(string reservationId, string bearerToken) =>
        GetAsync(Url($"/reservations/{reservationId}/guest-session-id"),
            new APIRequestContextOptions { Headers = Headers(bearerToken) });

    public Task<IAPIResponse> GetParticipantsAsync(string groupReservationId, string? bearerToken = null) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/sessions"),
            new APIRequestContextOptions { Headers = Headers(bearerToken) });

    public Task<IAPIResponse> GetGroupStatusAsync(string groupReservationId) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/status"));

    public Task<IAPIResponse> SubmitGroupReservationAsync(
        string groupReservationId, string reservationId, string? bearerToken = null) =>
        Api.PostAsync(Url($"/group-reservations/{groupReservationId}/submit"),
            new APIRequestContextOptions
            {
                DataString = Serialize(new SubmitGroupReservationRequest(reservationId)),
                Headers = Headers(bearerToken),
            });

    public Task<IAPIResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request) =>
        Api.PostAsync(Url("/guest/sessions"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(),
        });

    public Task<IAPIResponse> AddDishAsync(
        string guestSessionId, string dishId, int quantity, string sessionToken) =>
        Api.PostAsync(Url($"/guest/sessions/{guestSessionId}/dishes"), new APIRequestContextOptions
        {
            DataString = Serialize(new UpdateGuestDishRequest(dishId, quantity)),
            Headers = Headers(sessionToken),
        });

    public Task<IAPIResponse> SubmitGuestCartAsync(string guestSessionId, string sessionToken) =>
        Api.PostAsync(Url($"/guest/sessions/{guestSessionId}/submit"),
            new APIRequestContextOptions { Headers = Headers(sessionToken) });

    public Task<IAPIResponse> DeleteGuestSessionAsync(
        string groupReservationId, string guestSessionId, string bearerToken) =>
        DeleteAsync(Url($"/guest/groups/{groupReservationId}/sessions/{guestSessionId}"),
            new APIRequestContextOptions { Headers = Headers(bearerToken) });
}
