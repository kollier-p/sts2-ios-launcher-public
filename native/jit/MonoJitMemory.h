// Adapted from TheUnknownThing/StS2-Launcher-iOS (MIT); see upstream LICENSE and THIRD_PARTY_LICENSES.md.
// Stage 2 isolated source import: NOT wired into the production IPA build.
#pragma once
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Mono keeps execution addresses; emitters translate only the destination of writes. */
void sts2_jit_register(void *address, size_t size);
void sts2_jit_unregister(void *address);
void *sts2_jit_writable(void *address, size_t size);

#ifdef __cplusplus
}
#endif
