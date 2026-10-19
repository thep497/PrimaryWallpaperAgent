# Phase 2: one-shot wallpaper experiment

ผู้ใช้อนุญาตให้เปลี่ยน wallpaper และอ่าน source image แบบ exact path แล้วเมื่อ 19 ตุลาคม 2569 หลังบันทึก Phase 1 ใน Git commit `ad68266` งาน Phase 2 อยู่ใน branch `phase2-one-shot`

## ขอบเขต

- โหมดไม่มี argument และ `--json` ยังอ่านอย่างเดียว
- setter มีเฉพาะคำสั่ง explicit `trial-apply` / `trial-restore` เท่านั้น ไม่ใช้ monitor ID ว่างหรือ null
- เตรียม source photo สำเนา local ที่ตรวจ SHA-256 ตรงต้นฉบับ และ BMP ดำ 1×1 pixel ที่ตรวจ header/pixel แล้ว; สินทรัพย์และ state เฉพาะเครื่องไม่เข้า Git
- เก็บ pre-apply snapshot และ plan ก่อนตั้งภาพ จับคู่เฉพาะ active monitors ด้วย device path
- unknown wallpaper API entries นอก active GDI set ไม่เป็นเป้าหมายของ setter
- ถ้า topology/primary เปลี่ยนระหว่างเตรียมและ apply หรือ wallpaper เดิมถูกเปลี่ยน จะไม่เขียนทับ state ใหม่
- ถ้า setter ล้มเหลวหรือ readback ไม่ตรง จะพยายาม restore และตรวจกลับ หาก restore ไม่สำเร็จจะรายงานชัดเจน
- การทดลองนี้กำหนด Picture/Fill ที่มีอยู่เดิม ไม่แก้ background color, position หรือ slideshow settings
- ยังไม่มี watcher, tray หรือ autostart

## คำสั่งคืนค่า (ให้รันบน desktop ที่ต้องการคืนค่า)

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Restore-WallpaperTrial.ps1'
```

`-CheckOnly` ตรวจ assets/topology และ source file availability โดยไม่เรียก setter คำสั่ง restore ใช้ state ที่บันทึกใน `snapshots/phase2-current-plan.txt` และตั้งกลับด้วย monitor ID เดิมที่ยังต่ออยู่ ควรคง topology เดิมระหว่างทดลอง

คำสั่งคืนค่าตั้ง wallpaper ของ desktop ที่ API ให้เข้าถึง ณ เวลารัน ยังไม่รับรองว่าจะคืนค่าได้ทุก Virtual Desktop ด้วยการเรียกครั้งเดียว หากการทดลองกระทบมากกว่าหนึ่ง desktop ต้องตรวจและคืนค่าจากแต่ละ desktop แล้วดูภาพจริง

## ตรวจหลัง apply และหลังสลับ Virtual Desktop

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Inspect-WallpaperTrial.ps1' -Label 'P2-B'
```

รันจาก PowerShell ใหม่บน desktop ที่เลือก ก่อนกลับมาเปิด Codex เดิม บอกภาพที่เห็นจริงประกอบ JSON เสมอ Label เป็นชื่อที่ผู้ใช้เลือก ไม่ใช่ desktop ID ที่ตรวจอัตโนมัติ

## Validation

- ใช้ in-memory tests กับ 1/3/5 จอ และการ restore
- inject setter failure หลังตั้งสำเร็จบางจอ; ตรวจกลับว่า restore state เดิม
- inject API ที่ไม่สนใจ monitor ID และตั้งทุกจอพร้อมกัน; ต้องตรวจพบ readback ผิดและ restore
- inject restore failure; ต้องรายงานความผิดพลาด ไม่อ้างว่าคืนค่าสำเร็จ
- เปลี่ยน primary ก่อน/ระหว่าง apply, topology ไม่ stable และ wallpaper ถูกเปลี่ยนหลัง prepare
- Build ตั้ง TreatWarningsAsErrors; ผลการรันจริงต้องบันทึกแยกจากผล simulation

## สถานะก่อนทดลองจริง

- Build และ in-memory safety tests ผ่านแล้ว
- source image สำเนา local ขนาด 2,107,951 bytes และ SHA-256 ตรงต้นฉบับ
- black BMP header/dimensions/pixels ผ่านการตรวจ
- `trial-restore-check` ผ่านโดยไม่มี setter
- ยังรอ apply หนึ่งครั้งและผู้ใช้ยืนยันภาพบนจอจริง ก่อนทดสอบสลับ desktop

## แหล่งอ้างอิง

- [Microsoft SetWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
- [Microsoft GetWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getwallpaper)
- [Microsoft multiple desktops limitation](https://support.microsoft.com/en-us/windows/experience/personalization/change-the-desktop-background-in-windows)
