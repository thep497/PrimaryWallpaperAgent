# Bounded Console Guard test

## เหตุผล

Phase 2 พิสูจน์แล้วว่าตั้งภาพแยก monitor ได้ใน desktop ที่เลือก แต่หลังผู้ใช้สลับ desktop 1 -> desktop 2 -> desktop 1 ค่ากลับเป็นภาพเดิมทั้ง 3 จอ ตรงกันทั้ง API และการยืนยันภาพจริง primary/topology ไม่เปลี่ยน วิธี apply ครั้งเดียวจึงไม่พอกับการใช้งานหลาย Virtual Desktop บนเครื่องนี้

ขั้นนี้เพิ่ม guard สำหรับทดลองแบบจำกัดเวลาเท่านั้น ไม่ติดตั้ง tray/autostart ไม่สร้าง scheduled task หรือ service

## วิธีรัน

เปิด PowerShell แล้วรัน:

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Start-WallpaperGuard.ps1' -Seconds 120
```

เมื่อเห็น `BOUNDED GUARD` ให้สลับไป desktop 2 รอภาพนิ่ง แล้วกลับ desktop 1 รอภาพนิ่ง ดูว่าภาพกลับมาอยู่เฉพาะ primary และจออื่นกลับเป็นดำเองหรือไม่ อย่าสั่ง apply ซ้ำและอย่าเปิด guard ซ้อนกัน

- หยุดได้ด้วย Ctrl+C หรือรอครบ 120 วินาที; ตั้งช่วงทดลองได้ 10–300 วินาที
- หลังจบ process ค่าภาพล่าสุดยังอยู่; ไม่มี guard คอยแก้เมื่อสลับ desktop หลังจากนั้น
- ถ้าต้องการคืนค่า ให้หยุด guard ก่อนแล้วรัน Restore-WallpaperTrial.ps1 บน desktop ที่ต้องการตรวจคืนค่า
- ถ้าจะส่งผล snapshot จาก desktop 2 ให้รัน Inspect-WallpaperTrial.ps1 -Label G-B ใน PowerShell ใหม่บน desktop นั้นก่อนกลับมาเปิด Codex เดิม

## หลักการ

- อ่านทุก 1 วินาทีและรอ 2 snapshots ที่เหมือนกันก่อนแก้ ให้ Windows จัด topology และ wallpaper เสร็จก่อน
- ใช้ effective wallpaper state ที่ API รายงาน ไม่อ้างว่าตรวจพบ Virtual Desktop ID
- อ่าน primary และ monitor IDs ใหม่ทุกครั้ง รองรับจำนวนจอเปลี่ยนโดยไม่ใช้แบรนด์/เลข DISPLAY เป็นเงื่อนไข
- เขียนเฉพาะเมื่อภาพต่างจาก policy ไม่เขียนซ้ำทุก tick
- ใช้ภาพ local สำเนาที่ตรวจ hash แล้ว; ไม่เปิดอ่าน source directory หรือค้นภาพอื่น
- บันทึก before/after ใน transaction เดิมและ rollback เมื่อ setter/readback ล้มเหลว
- ใช้ mutex กัน guard/apply/restore จากโปรแกรมนี้ทำงานซ้อนกัน
- ถ้าพบภาพที่ผู้ใช้เปลี่ยนเป็นไฟล์อื่นที่ไม่อยู่ในแผน, mode ต่างจาก Picture/Fill หรือเกิด error จะหยุดให้ตรวจ
- จำกัด 8 repairs ต่อการทดลอง เพื่อหยุดหากมีการตั้งค่าตีกันต่อเนื่อง
- หลักฐานอยู่ใน snapshots ของ trial เดิม ใต้ guard-*; summary.json มี samples, repairs, stop reason และ snapshot ล่าสุด

## ข้อจำกัด

นี่เป็นการแก้หลัง Windows เปลี่ยนค่าแล้ว จึงอาจเห็นภาพเดิมแวบหนึ่ง โดยช่วงตรวจพบตามรอบอ่านประมาณ 1–2 วินาที รวมเวลา render ของ Windows แล้วต้องวัดจากการทดลองจริง ยังไม่รับรองว่าจอรองดำตลอดโดยไม่เกิด flash โดยเฉพาะกรณีฉายภาพที่ต้องป้องกันเนื้อหาส่วนตัวอย่างเคร่งครัด

ถ้าจอใหม่คืน wallpaper ที่ไม่อยู่ในชุดภาพที่รู้จัก หรือ monitor mapping ไม่ครบ guard รุ่นทดลองอาจหยุด ต้องทดสอบ Notebook เดี่ยว/สลับ primary/เสียบถอด/projector ก่อนสรุปว่าใช้งานจริงครบทุก topology ได้

ผล self-tests จำลองแยกจากผลบน Windows จริง: จำลอง desktop reset, primary เปลี่ยน, จำนวนจอเปลี่ยน, matching state ไม่เขียนซ้ำ และ unknown user image ต้องหยุด

## ผล smoke test บน Windows จริง

- วันที่ 19 ตุลาคม 2569 เวลา 15:31:44–15:31:54 (UTC+7), จำกัด 10 วินาที
- อ่าน 10 samples, repair 1 ครั้ง (setter requests 3 ครั้งสำหรับ active 3 จอ), exit code 0 และหยุดเองตามเวลา
- Snapshot ล่าสุด: primary LG ใช้ local photograph และอีกสองจอใช้ black BMP
- ไม่มี setter ซ้ำเมื่อ policy ตรงแล้ว หลักฐานอยู่ใน guard-20261019_083144_131-331794f0 ใต้ trial run เดิม
- ผลนี้ยังไม่ใช่การทดสอบสลับ Virtual Desktop ระหว่าง guard ทำงาน รอผู้ใช้ทดลองด้วยคำสั่ง 120 วินาทีด้านบน
