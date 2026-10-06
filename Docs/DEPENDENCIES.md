# Зависимости

## Среда

- Unity Editor 6000.6.4f1.
- Целевая платформа первого релиза: Windows 10/11 x64.
- Git и GitHub CLI для контроля версий; игра от них не зависит.

## Используемые Unity-пакеты

- Universal Render Pipeline 17.6.0.
- Input System 1.20.0.
- 2D Sprite, Tilemap и связанные пакеты из шаблона Universal 2D.
- Test Framework 1.8.0.
- Unity Pipeline 0.8.0-exp.1 используется только для автоматизации Editor и проверок, но не является runtime-зависимостью игровых механик.

Точные версии закреплены в `Packages/manifest.json` и `Packages/packages-lock.json`. Обновление Unity или пакетов требует отдельного решения в `Docs/DECISIONS.md`.

## Графика

- Внешний исходник: `C:\UnityProjects\Tanks_Pack\Tiger-II_strip2.png`.
- Копия проекта: `Assets/Game/Art/Tanks/TigerII/Source/Tiger-II_strip2.png`.
- Исходный файл не изменяется; Unity хранит параметры импорта и нарезки у проектной копии.
