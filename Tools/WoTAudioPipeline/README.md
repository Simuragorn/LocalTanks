# WoT audio pipeline

Конвейер извлекает только те Wwise-события, которые перечислены в `audio_clips.json`, декодирует их в WAV и кладёт результат в локальный Unity-каталог `Assets/LocalOnly/Resources/WoTAudio`. Все исходные и преобразованные аудиофайлы игнорируются Git.

## Первый запуск

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1
powershell -ExecutionPolicy Bypass -File Tools/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1
```

По умолчанию установка WoT ожидается в `C:\Games\World_of_Tanks_EU`. Другой путь можно передать через `-WoTRoot`. Повторный запуск использует локальный кэш; `-Force` заново извлекает банки и клипы.

После генерации откройте Unity или выполните `Local Tanks > Build Combat Assets`. `TankAudioController` загружает клипы по стабильным идентификаторам из боевых JSON. Если локальные файлы отсутствуют, игра продолжает работать без звука и без сломанных ссылок в префабах.

## Источник истины

- `audio_clips.json` — Wwise-события и выбранные варианты события; при необходимости `mediaBank` может отличаться от банка события;
- `Assets/Game/GameData/Definitions/Tanks/*.json` — соответствие танка клипам и игровая настройка громкости/тона;
- `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` — локальный отчёт с выбранным TXTP, числом вариантов и SHA-256 каждого результата.

MS-1 и Leichttraktor используют общий `eng_gaz_aa_pc`. Топовое 45-мм орудие MS-1 использует `wpn_small_pc`, а топовая автоматическая 20-мм Breda Leichttraktor — `wpn_automatic_pc`; звуки башен `tur_man_trav_04` и `tur_man_trav_05` остаются раздельными.
