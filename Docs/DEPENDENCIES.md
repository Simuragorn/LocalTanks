# Зависимости

## Среда

- Unity Editor 6000.6.4f1.
- Blender 4.3.2 — локальный headless-рендер 3D-моделей в 2D-спрайты; игра и её сборка от Blender не зависят.
- Целевая платформа первого релиза: Windows 10/11 x64.
- Git и GitHub CLI для контроля версий; игра от них не зависит.

## Локальный конвейер спрайтов

- [WoT-Blender-Toolkit](https://github.com/wotcuk/WoT-Blender-Toolkit), commit `7739e42655ae305d6bd3c00eeffedb7fdc6df33d`, лицензия GPL-3.0 — чтение структуры и ресурсов локальных `.pkg` в Blender 4.3.
- Локальная установка инструмента находится в `Tools/WoTSpritePipeline/ThirdParty/` и исключена из Git; закреплённая ревизия записывается в manifest каждого результата.
- Путь к установленной игре и Blender хранится только в `Tools/WoTSpritePipeline/config.local.json`.
- Пакеты игры, кэш импортёра, Blender-сцены и извлечённые текстуры не являются зависимостями runtime и не передаются в репозиторий. Финальные листы техники также остаются локальными и воспроизводятся конвейером при необходимости.
- Использование материалов третьей стороны не становится разрешённым из-за технической локальности конвейера; перед любой публикацией требуется отдельное разрешение правообладателя.

## Используемые Unity-пакеты

- Universal Render Pipeline 17.6.0.
- Input System 1.20.0.
- 2D Sprite, Tilemap и связанные пакеты из шаблона Universal 2D.
- Test Framework 1.8.0.
- Unity Pipeline 0.8.0-exp.1 используется только для автоматизации Editor и проверок, но не является runtime-зависимостью игровых механик.

Точные версии закреплены в `Packages/manifest.json` и `Packages/packages-lock.json`. Обновление Unity или пакетов требует отдельного решения в `Docs/DECISIONS.md`.

## Графика

- Единственный источник графики техники: `Assets/LocalOnly/WoTGenerated/<vehicle-id>/`.
- Финальный `_strip2.png`, manifest, layout, проверки поворота и Unity `.meta` находятся рядом; Unity использует этот спрайт непосредственно.
- `Assets/Game/Art/Tanks` удалён. Копировать или синхронизировать игровые листы между каталогами больше не требуется.
## Локальные инструменты, не входящие в Git

## Wwise-аудиоконвейер

- `wwiser v20260808` — разбор Wwise bank и генерация TXTP; официальный release `bnnm/wwiser`.
- `vgmstream r2117` — декодирование TXTP/WEM в PCM WAV; официальный release `vgmstream/vgmstream`.
- `QuickBMS 0.12.0` и закреплённый `wwise_pck_extractor.bms` commit `ae22a94e33fc981feafd53327ce9d3f4aa6afa0f` — выборочное извлечение Wwise `.pck`.
- Бинарники устанавливаются в `LocalData/WoTAudioPipeline/ThirdParty`, кэш — в `LocalData/WoTAudioPipeline/Work`; оба каталога исключены из Git.
- Unity не зависит от этих инструментов во время игры. Подготовленные клипы находятся в игнорируемом `Assets/LocalOnly/Resources/WoTAudio` и воспроизводятся обычным `AudioSource`.
- Установка и полный воспроизводимый запуск описаны в `Tools/WoTAudioPipeline/README.md`.

## Ollama

- Версия первого интеграционного прохода: `v0.40.2`, standalone Windows x64.
- Источник: официальный GitHub release `ollama/ollama`.
- Локальный путь: `LocalData/Ollama`.
- Модель: `qwen3:8b`, загружается из официальной библиотеки Ollama в `LocalData/OllamaModels`.
- Назначение: локальные стратегические решения и реплики ботов Sprint 007.
- Причина standalone-варианта: запуск только вместе с игрой без автозапуска фонового приложения при входе в Windows.
- Лицензии и бинарники стороннего инструмента не копируются в Git; установка воспроизводима по `Docs/LOCAL_LLM.md`.
