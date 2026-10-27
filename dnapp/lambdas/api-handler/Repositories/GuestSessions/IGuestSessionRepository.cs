using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public interface IGuestSessionRepository
{
    Task<int> CountByGroupReservationId(string groupReservationId);

    Task<GuestSession?> GetByGroupReservationId(string groupReservationId);

    Task<GuestSession?> GetByGuestSessionId(string id);

    Task<GuestSession?> GetByTokenHash(string tokenHash);

    Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId);

    Task<List<GuestSession>> GetAllByGroupReservationIdStronglyConsistentAsync(string groupReservationId);

    Task<List<GuestSession>> GetOwnerSessionsByGroupReservationIdStronglyConsistentAsync(string groupReservationId);

    Task<bool> TryMarkSubmittedAsync(string guestSessionId, string groupReservationId);

    Task<bool> TryUpdateDishesWhileGroupOpenAsync(GuestSession guestSession);

    Task<bool> TryDeleteWhileGroupOpenAsync(string guestSessionId, string groupReservationId);

    Task SaveAsync(GuestSession guestSession);

    Task DeleteAsync(GuestSession session);
}
