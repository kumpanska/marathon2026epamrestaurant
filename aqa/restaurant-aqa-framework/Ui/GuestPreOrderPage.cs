using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

public sealed record BrowserGuestSession(string Id, string Token);

public sealed class GuestPreOrderPage(IPage page) : BasePage(page)
{
    public ILocator NameInput => Page.GetByLabel("Enter Your Name");
    public ILocator BrowseMenuButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Browse Menu", Exact = true });
    public ILocator CartButton => Page.GetByRole(AriaRole.Button, new() { Name = "Open cart" });
    public ILocator CartDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Cart" });
    public ILocator SubmitButton =>
        CartDialog.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true });
    public ILocator SubmittedHeading =>
        Page.GetByRole(AriaRole.Heading, new() { Name = "Pre-Order Submitted!" });

    public async Task<BrowserGuestSession> AddDishAndSubmitAsync(
        string inviteToken,
        string guestName,
        string dishName,
        int quantity) =>
        await JoinAndAddDishAsync(inviteToken, guestName, dishName, quantity, submit: true);

    public async Task<BrowserGuestSession> JoinAndAddDishAsync(
        string inviteToken,
        string guestName,
        string dishName,
        int quantity,
        bool submit)
    {
        await Page.EvaluateAsync(
            """
            token => {
                const prefix = `guestPreorder:${token}:`;
                for (const key of Object.keys(localStorage)) {
                    if (key.startsWith(prefix)) localStorage.removeItem(key);
                }
            }
            """,
            inviteToken);
        await NavigateToAsync($"/join/{Uri.EscapeDataString(inviteToken)}");
        await NameInput.FillAsync(guestName);
        await BrowseMenuButton.ClickAsync();

        await AddDishThroughMenuAsync(dishName, quantity);

        BrowserGuestSession session = await Page.EvaluateAsync<BrowserGuestSession>(
            """
            token => ({
                id: localStorage.getItem(`guestPreorder:${token}:guestSessionId`),
                token: localStorage.getItem(`guestPreorder:${token}:guestSessionToken`)
            })
            """,
            inviteToken);
        if (string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Token))
            throw new InvalidOperationException("The guest session was not persisted after joining the group.");

        if (submit)
            await SubmitBasketAsync();

        return session;
    }

    public async Task AddDishToExistingSessionAsync(
        string inviteToken,
        string guestName,
        string guestSessionId,
        string sessionToken,
        string dishName,
        int quantity)
    {
        await NavigateToAsync("/");
        await Page.EvaluateAsync(
            """
            session => {
                const prefix = `guestPreorder:${session.inviteToken}:`;
                localStorage.setItem(`${prefix}guestName`, session.guestName);
                localStorage.setItem(`${prefix}cart`, '[]');
                localStorage.setItem(`${prefix}guestSessionId`, session.guestSessionId);
                localStorage.setItem(`${prefix}guestSessionToken`, session.sessionToken);
            }
            """,
            new { inviteToken, guestName, guestSessionId, sessionToken });
        await NavigateToAsync($"/join/{Uri.EscapeDataString(inviteToken)}");
        await AddDishThroughMenuAsync(dishName, quantity);
        await SubmitBasketAsync();
    }

    private async Task AddDishThroughMenuAsync(string dishName, int quantity)
    {
        var dish = Page.Locator("app-dish-card").Filter(new() { HasText = dishName });
        await dish.WaitForAsync();
        var addDishButton = dish.GetByRole(AriaRole.Button, new() { Name = "Pre-Order", Exact = true });
        for (var portion = 0; portion < quantity; portion++)
            await addDishButton.ClickAsync();
    }

    private async Task SubmitBasketAsync()
    {
        await CartButton.ClickAsync();
        await SubmitButton.ClickAsync();
        await SubmittedHeading.WaitForAsync();
    }
}
