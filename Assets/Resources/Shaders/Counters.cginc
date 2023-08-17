#ifndef COUNTERS_H
#define COUNTERS_H

RWStructuredBuffer<uint> _DebugCounters;

enum DebugCounterType {
    DEBUG_COUNTER_RAYS,
    DEBUG_COUNTER_SHADOW_RAYS,

    DEBUG_COUNTER_SHIFT_ATTEMPTS,
    DEBUG_COUNTER_SHIFT_SUCCESSES,
};

void IncrementCounter(DebugCounterType counter) {
    InterlockedAdd(_DebugCounters[counter], 1);
}

#endif