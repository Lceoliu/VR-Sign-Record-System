---
status: accepted
---

# Simplify the key and button phases while preserving V1 capture shape

Phase 4 now opens the chest and asks the Participant to choose among all three visible keys, while Phase 5 opens the cabinet and directly evaluates the existing one-, two-, or three-button Task Variant. The former four-colour chest sequence and Phase 5 key prerequisite are removed because they duplicate the recorded Task Variants and prevent the instruction-visible tasks from being immediately actionable. V1 fields such as `chest_button_order` remain serialized but are deprecated and ignored at runtime so existing artifact readers do not break.
