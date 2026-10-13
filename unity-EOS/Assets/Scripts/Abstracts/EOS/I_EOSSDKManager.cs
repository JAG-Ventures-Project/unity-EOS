using Epic.OnlineServices;
using Epic.OnlineServices.Platform;

public interface I_EOSSDKManager
{
    EosApiKey EosApiKey { get; set; }
    PlatformInterface GetEOSPlatformInterface();
    Result InitializePlatformInterface();
    PlatformInterface CreatePlatformInterface();
    bool ShutDownEOSSDK();
}