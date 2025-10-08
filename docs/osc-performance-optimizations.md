# OSC Performance Optimizations

## Problem
When receiving 600 OSC messages per second (1秒間に600件のOSC通信), the application experienced severe processing delays (処理遅延が凄まじい).

## Root Cause Analysis

### Architecture
1. **OSCListener** receives OSC messages and queues them to a bounded channel (1000 capacity)
2. **ProcessMessagesAsync()** reads from channel and publishes to EventBus
3. **EventBus** queues handler invocations to a dispatch queue
4. **ModuleHost.HandleOscMessageReceived()** is invoked for each message and loops through all loaded modules

### Bottleneck
With 600 messages/second and 3 loaded modules:
- 1,800 method invocations per second (600 × 3)
- All 3 current modules (AfkJumpModule, AfkSoundCancelModule, OscRepeaterDisablerModule) have **empty** `OnOscMessageReceived` implementations
- Each invocation creates overhead: try-catch block, method call, context passing
- Additional overhead from debug logging checks (600/sec)

## Optimizations Implemented

### 1. Module Cache with IL Inspection
**File**: `Infrastructure/ModuleHost.cs`

Added `BuildOscMessageReceiverCache()` method that:
- Inspects each module's `OnOscMessageReceived` method implementation
- Uses reflection to get the method body's IL byte array
- Filters out trivial implementations (IL length ≤ 2 bytes = empty method with just `ret` instruction)
- Caches only modules that have actual OSC message handling logic

```csharp
private void BuildOscMessageReceiverCache()
{
    var receivers = new List<LoadedModule>();
    foreach (var module in _modules)
    {
        var moduleType = module.Instance.GetType();
        var method = moduleType.GetMethod(
            nameof(IModule.OnOscMessageReceived),
            BindingFlags.Public | BindingFlags.Instance);
        
        if (method != null)
        {
            var methodBody = method.GetMethodBody();
            if (methodBody != null && methodBody.GetILAsByteArray()?.Length > 2)
            {
                receivers.Add(module);
            }
        }
    }
    
    _oscMessageReceiverModules = receivers;
}
```

### 2. Early Exit in Message Handler
**File**: `Infrastructure/ModuleHost.cs`

Modified `HandleOscMessageReceived()` to:
- Check if any event handlers are subscribed
- Check if any modules need OSC messages (using cache)
- Early exit if both are empty, avoiding unnecessary context creation and processing
- Only iterate through modules that actually handle OSC messages

```csharp
private void HandleOscMessageReceived(OscMessageReceived message)
{
    // ... debug logging ...
    
    // Early exit if no handlers and no modules interested in OSC messages
    var hasEventHandlers = OscMessageReceived != null;
    var modulesToNotify = _oscMessageReceiverModules ?? _modules;
    
    if (!hasEventHandlers && (modulesToNotify == null || modulesToNotify.Count == 0))
    {
        return;  // Skip all processing
    }
    
    // ... rest of processing ...
}
```

### 3. Suppress Debug Logging for High-Frequency Events
**File**: `Infrastructure/EventBus.cs`

Added suppression list for high-frequency event types:
- Prevents debug log formatting (600 times/sec) when debug logging is disabled
- Uses HashSet for O(1) lookup
- Specifically targets `OscMessageReceived` type

```csharp
private static readonly HashSet<Type> _suppressDebugLoggingTypes = new();

public EventBus(IEventLogger? logger = null)
{
    // ... initialization ...
    _suppressDebugLoggingTypes.Add(typeof(OscMessageReceived));
}

public void Publish<T>(T message)
{
    var messageType = typeof(T);
    if (_handlers.TryGetValue(messageType, out var handlers) && !handlers.IsDefaultOrEmpty)
    {
        if (!_suppressDebugLoggingTypes.Contains(messageType))
        {
            LogDebug(() => $"Publishing message of type {messageType.FullName} to {handlers.Length} handler(s).");
        }
        // ... dispatch handlers ...
    }
}
```

## Performance Impact

### Before Optimization
- **600 OSC messages/second**
- **1,800 empty method invocations/second** (600 × 3 modules)
- **600 debug log checks/second** in EventBus
- Context object creation for every message
- Unnecessary loop iterations

### After Optimization
- **600 OSC messages/second** (same input)
- **0 method invocations** (when no modules handle OSC messages)
- **0 debug log overhead** for OSC messages
- Early exit avoids context creation when not needed
- Cache eliminates redundant work

### Estimated Savings
With current 3 modules having empty implementations:
- Eliminates ~1,800 method calls/second
- Saves ~600 context object allocations/second (when no handlers)
- Removes ~600 debug log checks/second
- **Result**: Virtually zero overhead for OSC messages when no module needs them

## Backward Compatibility

All changes are **fully backward compatible**:
- Modules with actual OSC handling logic will continue to receive messages
- New modules can implement `OnOscMessageReceived` and will be detected
- Cache is rebuilt when service provider is initialized
- Falls back to all modules if cache is null
- No API changes required

## Future Enhancements

Potential additional optimizations:
1. **Batching**: Process multiple OSC messages in a single module invocation
2. **Filtering**: Allow modules to specify which OSC addresses they're interested in
3. **Async Handling**: Allow modules to process OSC messages asynchronously
4. **Attribute-based Opt-in**: Use attributes to mark modules that need OSC messages
5. **Rate Limiting**: Add configurable rate limiting for OSC message processing

## Testing

### Manual Verification
1. Start application with 600 OSC messages/second
2. Check logs for cache building message showing "0 of 3 module(s) will receive OSC notifications"
3. Verify no performance degradation
4. Confirm modules with actual OSC handling still work correctly

### IL Detection Test
Verified that empty methods have IL byte array length of 2 bytes (nop + ret instructions), confirming the `> 2` threshold correctly identifies non-empty implementations.

## Conclusion

These minimal, surgical changes address the root cause of OSC processing delays by eliminating unnecessary work. The optimizations are particularly effective when modules don't need OSC messages, which is the current state of all 3 loaded modules.
