using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

public sealed class GroupOrderPage(IPage page) : BasePage(page)
{
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order" });
    public ILocator ErrorHeading => Page.Locator(".error-panel h1");
    public ILocator StatusBadge => Page.Locator(".status-badge");
    public ILocator PendingNotice => Page.Locator(".pending-notice");
    public ILocator EmptyState => Page.Locator(".empty-state");
    public ILocator SubmitButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true });
    public ILocator ConfirmationDialog =>
        Page.GetByRole(AriaRole.Dialog, new() { Name = "Submit this pre-order?" });
    public ILocator ConfirmationSubmitButton =>
        ConfirmationDialog.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true });

    public Task OpenFromReservationsAsync() => OpenFromReservationsAsync(0);

    public async Task OpenFromReservationsAsync(int reservationIndex)
    {
        var reservations = new ReservationDashboardPage(Page);
        await reservations.NavigateAsync();
        await reservations.Reservation(reservationIndex).OpenGroupPreOrderAsync();
        await Heading.WaitForAsync();
    }

    public async Task NavigateToReservationAsync(string reservationId)
    {
        await NavigateToAsync($"/reservations/{Uri.EscapeDataString(reservationId)}/group-order");
    }

    public async Task ConfirmAsync()
    {
        await SubmitButton.ClickAsync();
        await ConfirmationSubmitButton.ClickAsync();
    }

    public ILocator ConsolidatedDish(string dishName) =>
        Page.Locator(".consolidated-items li").Filter(new() { HasText = dishName });

    public ILocator Participant(string guestName) =>
        Page.Locator("app-participant-card").Filter(new() { HasText = guestName });
}
