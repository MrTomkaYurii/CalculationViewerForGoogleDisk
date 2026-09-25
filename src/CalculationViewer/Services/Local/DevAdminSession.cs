namespace CalculationViewer.Services.Local;

/// <summary>Заглушка входу для розробки інтерфейсу: одна кнопка, без реальної автентифікації. У продакшені: Firebase Authentication.</summary>
internal sealed class DevAdminSession : IAdminSession
{
    public event Action? Changed;
    public bool IsSignedIn { get; private set; }
    public string? Email => IsSignedIn ? "tomka.yuriy@gmail.com" : null;

    public async Task SignInAsync()
    {
        await Task.Delay(400);
        IsSignedIn = true;
        Changed?.Invoke();
    }

    public Task SignOutAsync()
    {
        IsSignedIn = false;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
