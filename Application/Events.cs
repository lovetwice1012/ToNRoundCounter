namespace ToNRoundCounter.Application
{
    public record WebSocketConnected;
    public record WebSocketDisconnected;
    public record WebSocketMessageReceived(string Message);
    public record OscMessageReceived(Rug.Osc.OscMessage Message);
    public record OscConnected;
    public record OscDisconnected;
    public record SettingsValidationFailed(System.Collections.Generic.IEnumerable<string> Errors);
    public record ModuleLoadFailed(string File, System.Exception Exception);
}
