using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Networking.Transport;

[assembly: RegisterGenericJobType(typeof(SimpleConnectionLayer.ReceiveJob<UnderlyingConnectionList>))]
[assembly: RegisterGenericJobType(typeof(SimpleConnectionLayer.ReceiveJob<NullUnderlyingConnectionList>))]
[assembly: RegisterGenericJobType(typeof(RelayLayer.ReceiveJob<UnderlyingConnectionList>))]
[assembly: RegisterGenericJobType(typeof(RelayLayer.ReceiveJob<NullUnderlyingConnectionList>))]

[assembly: InternalsVisibleTo("Unity.Networking.Transport.Editor.Tests")]
[assembly: InternalsVisibleTo("Unity.Networking.Transport.Runtime.Tests")]

[assembly: InternalsVisibleTo("Unity.Networking.Transport.NetcodeInterop")]

// N4E accesses a few of our internal APIs.
[assembly: InternalsVisibleTo("Unity.NetCode")]
