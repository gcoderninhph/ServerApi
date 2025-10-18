# Client.Asp.net - Demo Client cho ServerApi

Demo client application s? d?ng **ClientCore** v?i pattern tuong t? nhu Asp.net server, s? d?ng Dependency Injection v� RegisterServices.

## T?ng quan

Client.Asp.net l� m?t console application minh h?a c�ch s? d?ng ClientCore d? k?t n?i d?n ServerApi th�ng qua:
- **WebSocket** - ws://localhost:5000/ws  
- **TCP Stream** - localhost:5001

## C?u tr�c

**RegisterServices**: Tuong t? server, qu?n l� lifecycle v� cung c?p helper methods d? connect v� send messages.

## Ch?y ?ng d?ng

1. Start Server: `cd Asp.net && dotnet run`
2. Start Client: `cd Client.Asp.net && dotnet run`
3. Ch?n test scenario (1-4)

## Test Scenarios

1. **WebSocket Client** - Connect, send pings v� messages qua WebSocket
2. **TCP Stream Client** - Connect, send pings v� messages qua TCP
3. **Both Protocols (Parallel)** - Test c? 2 protocols d?ng th?i
4. **WebSocket with Headers & Query Params** - Demo authentication headers v� query parameters

## L?i �ch Pattern M?i

-  Dependency Injection v?i RegisterServices
-  Lifecycle events (OnConnect/OnDisconnect)
-  Helper methods: ConnectWebSocketAsync(), SendWebSocketPingAsync(), etc.
-  Type-safe API  
-  Testability

Xem CONNECT_USAGE_GUIDE.md trong ClientCore d? bi?t th�m chi ti?t.
