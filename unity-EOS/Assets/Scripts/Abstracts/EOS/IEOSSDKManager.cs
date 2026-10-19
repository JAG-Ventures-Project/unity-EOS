using Epic.OnlineServices;
using Epic.OnlineServices.Platform;

namespace EOS
{
    internal interface IEOSSDKManager
    {
        EosApiKey EosApiKey { get; set; }
        PlatformInterface GetEOSPlatformInterface();
        ProductUserId GetLocalProductUserId();
        Result InitializePlatformInterface();
        PlatformInterface CreatePlatformInterface();
        bool ShutDownEOSSDK();
    }
}