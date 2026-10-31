using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Tables;
using Moq;

namespace APIHandler.Tests.Services;

public class TableServiceTests
{
    [Fact]
    public async Task GetAvailableTablesAsync_ReservationOverlapsRequestedSlot_ExcludesTable()
    {
        var startTime = new DateTime(2026, 11, 5, 19, 0, 0, DateTimeKind.Utc);
        var table = CreateTable();
        var tableRepository = new Mock<ITableRepository>();
        tableRepository.Setup(repository => repository.GetAllTablesAsync())
            .ReturnsAsync(new List<Table> { table });
        var reservationRepository = new Mock<IReservationRepository>();
        reservationRepository.Setup(repository => repository.GetReservationsByTableIdAsync(table.Id))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(startTime, startTime.AddHours(1), ReservationStatus.Reserved)
            });

        var service = new TableService(tableRepository.Object, reservationRepository.Object);

        var result = await service.GetAvailableTablesAsync(null, 4, startTime);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailableTablesAsync_ReservationStartsAfterRequestedSlot_IncludesTable()
    {
        var startTime = new DateTime(2026, 11, 5, 19, 0, 0, DateTimeKind.Utc);
        var table = CreateTable();
        var tableRepository = new Mock<ITableRepository>();
        tableRepository.Setup(repository => repository.GetAllTablesAsync())
            .ReturnsAsync(new List<Table> { table });
        var reservationRepository = new Mock<IReservationRepository>();
        reservationRepository.Setup(repository => repository.GetReservationsByTableIdAsync(table.Id))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(startTime.AddHours(2), startTime.AddHours(3), ReservationStatus.Reserved)
            });

        var service = new TableService(tableRepository.Object, reservationRepository.Object);

        var result = await service.GetAvailableTablesAsync(null, 4, startTime);

        Assert.Contains(table, result);
    }

    private static Table CreateTable() => new()
    {
        Id = "table-1",
        LocationId = "location-1",
        Capacity = 8,
        TableNumber = 1
    };

    private static Reservation CreateReservation(DateTime startTime, DateTime endTime, ReservationStatus status) => new()
    {
        Id = "reservation-1",
        TableId = "table-1",
        WaiterId = "waiter-1",
        StartTime = startTime,
        EndTime = endTime,
        GuestCount = 4,
        Status = status
    };
}
