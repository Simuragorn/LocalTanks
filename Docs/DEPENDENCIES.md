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
- Пакеты игры, кэш импортёра, Blender-сцены и извлечённые текстуры не являются зависимостями runtime и не передаются в репозиторий. Готовый лист Tiger II добавлен в локальную историю проекта как явно согласованное исключение для закрытого тестирования.
- Использование материалов третьей стороны не становится разрешённым из-за технической локальности конвейера; перед любой публикацией требуется отдельное разрешение правообладателя.

## Используемые Unity-пакеты

- Universal Render Pipeline 17.6.0.
- Input System 1.20.0.
- 2D Sprite, Tilemap и связанные пакеты из шаблона Universal 2D.
- Test Framework 1.8.0.
- Unity Pipeline 0.8.0-exp.1 используется только для автоматизации Editor и проверок, но не является runtime-зависимостью игровых механик.

Точные версии закреплены в `Packages/manifest.json` и `Packages/packages-lock.json`. Обновление Unity или пакетов требует отдельного решения в `Docs/DECISIONS.md`.

## Графика

- Локальный результат конвейера: `Assets/LocalOnly/WoTGenerated/G16_PzVIB_Tiger_II/G16_PzVIB_Tiger_II_strip2.png`.
- Игровая копия Tiger II: `Assets/Game/Art/Tanks/TigerII/Source/Tiger-II_strip2.png`.
- Manifest и layout результата остаются рядом с локальным рендером; Unity хранит собственные параметры импорта и нарезки у игровой копии.
- Остальные экспериментальные спрайты из 3D-моделей сохраняются только в `Assets/LocalOnly/WoTGenerated/` и не заменяют отслеживаемую проектную графику автоматически.
