# ND-120 (DELILAH-L) DIRECT TRA IIC golden - IOX vs level-14(MOR) encoding

Executes a macro TRA IIC (150005 octal) directly, under a controlled PIC state, and reads
A = the IIC returned. IOX = detect bit 10 (hivec 2); MOR = bit 12 (hivec 4); level-14
request = bit 14 (hivec 6). Expected IIC codes: IOX=7, MOR=11 oct(9 dec).

## A: IOX only (bit10/hivec2)
```
  IIE set via TRR IIE: A=177777
  setup : raised pending=2000  detect=175777 Am2914mask=177777
  after : detect=177777 Am2914mask=177777  (detect change = what TRA IIC cleared)
  => TRA IIC returned A = 0 oct = 0 dec  (other)
```

## B: IOX + level-14 req (bit10 + bit14/hivec6)
```
  IIE set via TRR IIE: A=177777
  setup : raised pending=42000  detect=135777 Am2914mask=177777
  after : detect=137777 Am2914mask=177777  (detect change = what TRA IIC cleared)
  => TRA IIC returned A = 0 oct = 0 dec  (other)
```

## C: level-14 req only (bit14/hivec6)
```
  IIE set via TRR IIE: A=177777
  setup : raised pending=40000  detect=137777 Am2914mask=177777
  after : detect=137777 Am2914mask=177777  (detect change = what TRA IIC cleared)
  => TRA IIC returned A = 0 oct = 0 dec  (other)
```

## D: MOR only (bit12/hivec4)
```
  IIE set via TRR IIE: A=177777
  setup : raised pending=10000  detect=167777 Am2914mask=177777
  after : detect=177777 Am2914mask=177777  (detect change = what TRA IIC cleared)
  => TRA IIC returned A = 0 oct = 0 dec  (other)
```

