# ND-120 (DELILAH-L) internal-interrupt (IIC) path trace - RUN / IOX-ERROR window

Golden from the C# oracle (RUN passes IOX-ERROR). Window: DUMMY OUTPUT -> IOX-ERROR -> LEVEL 13.

Two-chip Am2914: HIGH=levels 8-15, LOW=levels 0-7. Detect active-LOW (0=pending); Mask active-LOW
(1=masked). Columns show BOTH raw chip value and the ND-100 active-high derivation:

- **IID** = `detect(raw octal) / ~detect(pending, octal)`  (pending bits = internal-interrupt IREQ inputs)
- **IIE** = `mask(raw octal) / ~mask(enabled, octal)`
- **IID & IIE** = pending AND enabled, with known sources named
  (IOX = IREQ bit10/hivec2 -> IIC 7; MOR = IREQ bit12/hivec4 -> IIC 11 oct).
- **hi bit** = highest set bit of IID&IIE (the winning IREQ bit, Am2914 space - NOT the IIC code).
- **IIC ret (A)** = A register AFTER TRA IIC completes = the microcode's translated ND-100 IIC code.

Divergence test: if IID&IIE matches your port but `IIC ret (A)` differs, the bug is the vector->code
mapping in the AIIC scan; if IID&IIE itself differs (wrong/absent IREQ bit), it is source detection.

| # | trigger | CSAR + DELILAH-L sym | PIL | IID detect/pend | IIE mask/en | IID&IIE [src] | hi bit | IIC ret (A) |
|---|---------|----------------------|-----|-----------------|-------------|---------------|--------|-------------|

<details><summary>RUN console transcript (tail)</summary>

```
RUN
RUN


== RUNNING TESTS ON LEVEL 1 ==

== CLOCK STARTED             ==


== DUMMY OUTPUT STARTED      ==


== IOX-ERROR STARTED         ==


== LEVEL 13 (OCT.) STARTED   ==

== ARGUMENT INSTRUCTIONS                
```
</details>

