# Using Netcode for GameObjects transports

[Netcode for GameObjects](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest) supports using custom transports to alter how low-level communications are handled. This is done through the [`NetworkTransport`](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest?subfolder=/api/Unity.Netcode.NetworkTransport.html) API. The community has provided [a number of implementations](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/Transports) for third-party networking libraries and services.

Starting with Unity 6.6, it's possible to use these transports in Unity Transport. This is useful for projects that don't use Netcode for GameObjects but still want to benefit from third-party transports written for it.

## Wrapping a `NetworkTransport` in Unity Transport

To use a custom `NetworkTransport`, you need to create a `NetworkDriver` that uses a `NetworkTransportInterface` constructed with your custom `NetworkTransport`. Note that `NetworkTransport` implementations are necessarily `MonoBehaviour`s and if your transport is not already attached to a GameObject, you will need to create one. The following code is an example of creating the GameObject, the network interface, and the driver that will use the interface:

```csharp
using Unity.Networking.Transport.NetcodeInterop;
using UnityEngine;

...

// Create a GameObject to hold the NetworkTransport.
var go = new GameObject("NetcodeTransport");
GameObject.DontDestroyOnLoad(go);

// Add your custom transport to the GameObject.
var transport = go.AddComponent<MyNetcodeTransport>();

// Create a network interface wrapping the transport.
var netif = new NetworkTransportInterface(transport);

// Create a driver that uses that network interface.
var driver = NetworkDriver.Create(netif);
```

The resulting `NetworkDriver` can be used in exactly the same ways as one created with the UDP or WebSocket network interfaces.

## Usage in Netcode for Entities

Because Netcode for Entities relies on Unity Transport for low-level communications, the ability to use Netcode for GameObjects transports in Unity Transport allows using them in Netcode for Entities too. To do so requires providing a custom driver constructor. More details are available [in the Netcode for Entities documentation](https://docs.unity3d.com/Packages/com.unity.netcode@latest?subfolder=/manual/networking-network-drivers.html). The following code is a simple example of using Netcode for GameObjects transports with Netcode for Entities:

```csharp
public class MyCustomTransportDriverConstructor : INetworkStreamDriverConstructor
{
    // Will need to be set before assigning to NetworkStreamReceiveSystem.
    public MyCustomNetworkTransport MyTransport;

    public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
        var netif = new NetworkTransportInterface(MyTransport);
        var driverInstance = DefaultDriverBuilder.CreateClientNetworkDriver(netif);
        driverStore.RegisterDriver(TransportType.Socket, driverInstance);
    }

    public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
        var netif = new NetworkTransportInterface(MyTransport);
        var driverInstance = DefaultDriverBuilder.CreateServerNetworkDriver(netif);
        driverStore.RegisterDriver(TransportType.Socket, driverInstance);
    }
}
```

## `NetworkTransport` versus `INetworkInterface`

Unity Transport already has a mechanism to support replacing its low-level networking operations by implementing the `INetworkInterface` interface. Supporting `NetworkTransport` in addition to `INetworkInterface` might seem unnecessary, since it introduces two ways to integrate third-party networking libraries and services into Unity Transport. The following information clarifies when to use each one.

The main differences between `NetworkTransport` and `INetworkInterface` are:

* `INetworkInterface` relies on Unity's job system to perform work outside the main thread, while `NetworkTransport` always runs on the main thread. This has performance implications since using a `NetworkTransport` will force Unity Transport to synchronize with the main thread more often.
* `INetworkInterface` has a simple packet-based API, you get a list of packets to send, and provide a list of packets received, while `NetworkTransport` offers a more robust connection-oriented API.
* `INetworkInterface` only deals with unmanaged types that are compatible with Burst while `NetworkTransport` relies on managed types.

Consider the following architecture needs to help choose which functionality to use:

* If you need to implement a complex mechanism to establish connections (handshake, approval, etc.), use `NetworkTransport`. If you only need to integrate a very socket-like API, use `INetworkInterface`.
* If you need to scale to many (10+) concurrent players, or require the best performance possible, use `INetworkInterface` and try to use Burst-compiled jobs to perform all the work.
