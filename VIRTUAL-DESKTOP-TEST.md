# Virtual Desktop read-only test

## สถานะ

- ผู้ใช้ยืนยันว่ามี Virtual Desktop ตั้งแต่ 2 อันขึ้นไป
- จอที่ใช้งาน 3 จอ: LG เป็น primary; ผู้ใช้ยืนยันผล Phase 1 แล้ว
- เก็บ A เวลา 15:07:06 และ B เวลา 15:08:38 วันที่ 19 ตุลาคม 2569 (UTC+7) แล้ว
- Active monitor device paths, primary, bounds และ wallpaper paths ตรงกันทั้ง 3 จอ; background RGB, position และ slideshow status ตรงกัน
- ทั้งสอง capture topology คงที่ และยังมีข้อสังเกต 7 รายการเดิม
- ผู้ใช้ยืนยันว่าทั้ง 3 จอในชุด B เห็นภาพเดิมเหมือนชุด A
- ผลเปรียบเทียบพร้อม SHA-256 ของ input อยู่ใน `snapshots/virtual-desktop-A-B-comparison.json`; ไม่เขียนทับ snapshot ต้นฉบับ
- ยืนยันได้เฉพาะผลอ่านในกรณีสอง desktop ใช้ภาพเดียวกัน ยังไม่ได้ทดสอบภาพต่างกันหรือการเขียนภาพแยกจอ

## ข้อจำกัดที่พบจากเอกสารและขั้นถัดไปที่เสนอ

Microsoft Support ระบุว่า เมื่อใช้หลาย desktop การตั้ง background picture แยกตาม monitor ไม่รองรับ ข้อนี้เป็นข้อจำกัดที่ผู้ผลิตระบุ ไม่ใช่หลักฐานว่า COM setter จะล้มเหลวบนเครื่องนี้ เพราะยังไม่ได้ทดลองเขียน

จึงยังไม่เพิ่ม watcher/tray/autostart ผู้ใช้อนุญาตเข้าสู่ Phase 2 แล้ว: ทดลอง apply หนึ่งครั้งให้ภาพเดิมบน primary และภาพดำบน secondary จากนั้นผู้ใช้สลับ desktop เพื่อดูว่าคงค่า ถูกใช้ร่วมกัน หรือถูก Windows แทนที่ แล้วคืนค่าเดิม

แผนข้อเสนอเดิมใน `plans/phase2-one-shot-proposal.json` เก็บไว้เป็นประวัติก่อนอนุญาต ส่วน run plan ที่ใช้จริงอ้างจาก `snapshots/phase2-current-plan.txt` พร้อม pre-apply state และ source image สำเนาที่ตรวจ hash แล้ว

หลังได้รับอนุญาต source image แบบ exact path ได้ทดลอง apply สำเร็จเมื่อ 15:23:28 วันที่ 19 ตุลาคม 2569; readback และภาพจริงตรงแผน แต่เมื่อสลับไป desktop 2 และกลับ desktop 1 ภาพกลับเป็นภาพเดิมทั้ง 3 จอ ตรงกันทั้ง snapshot และคำยืนยันผู้ใช้ จึงพิสูจน์ข้อจำกัดการคงค่าของ one-shot แล้ว ดู [PHASE2-TRIAL.md](PHASE2-TRIAL.md) และขั้นถัดไป [bounded Console Guard](CONSOLE-GUARD-TEST.md)

## ขอบเขต

`Capture-DesktopSnapshot.ps1` เรียกเฉพาะ Probe แบบ read-only แล้วบันทึก JSON ใน `snapshots/` โดยใช้ชื่อที่ไม่เขียนทับหลักฐานเดิม ไม่มีการตั้ง wallpaper สลับ desktop หรือแก้ execution policy

Label `VD-A` / `VD-B` เป็นชื่อที่ผู้ใช้กำหนดตามขั้นตอนทดลอง ไม่ใช่ Virtual Desktop ID ที่ API ตรวจพบ หน้าต่างที่อยู่ทุก desktop อาจทำให้การอาศัยหน้าต่างเป็นตัวระบุ desktop คลุมเครือ จึงใช้การเลือก desktop และการยืนยันภาพจริงของผู้ใช้เป็นหลักฐานประกอบ

## เก็บชุด B

1. กด `Win+Tab` แล้วเลือก Virtual Desktop อื่นที่มีอยู่แล้ว ถ้ามี desktop ที่ใช้ภาพต่างกันอยู่แล้ว ให้เลือกอันนั้น โดยยังไม่ตั้งภาพใหม่
2. เปิด PowerShell ใหม่บน desktop นั้น อย่าเปิด VS Code หรือ Codex หน้าต่างเก่าที่อาจพากลับ desktop เดิม
3. รัน:

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Capture-DesktopSnapshot.ps1' -Label 'VD-B'
```

4. ดูภาพจริงบนทั้ง 3 จอ แล้วค่อยกลับมาบอกว่าสลับแล้ว และภาพแต่ละจอเหมือน/ต่างจากชุด A อย่างไร
5. Exit code 2 ที่แสดงในข้อมูล Probe ยังเป็นผล REVIEW จากรายการ monitor ส่วนเกิน ไม่ได้แปลว่าสคริปต์เก็บไฟล์ล้มเหลว ถ้ามี `Saved snapshot` แสดงว่าเก็บสำเร็จ

## เกณฑ์ประเมิน

- เทียบรายการ active monitors ด้วย device path และตรวจว่า primary/topology ไม่เปลี่ยน
- เทียบ wallpaper path, background RGB, position และภาพที่ผู้ใช้เห็นจริง
- ถ้า API เปลี่ยนตามภาพที่เปลี่ยน: เป็นหลักฐานเฉพาะกรณีอ่านภาพ ณ desktop ที่เลือก ยังไม่พิสูจน์การ apply policy
- ถ้าภาพต่างแต่ API คืนค่าเดิม: บันทึกเป็นข้อจำกัดของการอ่านผ่าน API บน Windows รุ่นนี้และทบทวนการออกแบบก่อนเพิ่ม watcher
- ถ้าทั้งสอง desktop ใช้ภาพเหมือนกัน: ผลนี้ยังไม่พิสูจน์กรณี wallpaper ต่างกัน ต้องกำหนดการทดลองเพิ่มเติมกับผู้ใช้ โดย Phase 1 ยังไม่เปลี่ยน wallpaper
- ถ้า primary/topology ต่างกันหรือผู้ใช้ไม่แน่ใจว่า capture จาก desktop ไหน ให้เก็บใหม่ ไม่สรุปผลแทนหลักฐานที่ขาด

## อ้างอิง

- [Microsoft IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager): API จัดการการเชื่อมโยงหน้าต่างกับ desktop และแนะนำให้ผู้ใช้เป็นผู้เริ่มการสลับ desktop
- [Microsoft IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper): API wallpaper ที่ Probe ใช้
- [Microsoft Change the desktop background in Windows](https://support.microsoft.com/en-us/windows/experience/personalization/change-the-desktop-background-in-windows): หัวข้อ Desktop background and multiple desktops ระบุข้อจำกัดการตั้งภาพแยก monitor
