# Схема игровых данных

Статус: реализованная схема Sprint 002. JSON является источником истины, а импортёр создаёт runtime-ассеты.

## Общие правила

- Формат: UTF-8 JSON.
- Идентификаторы: `snake_case`, уникальны внутри всей боевой базы.
- Десятичный разделитель: точка.
- Все ссылки выполняются по ID, а не по пути Unity-ассета.
- Каждый файл содержит `schemaVersion`.
- Неизвестные обязательные версии отклоняются валидатором.
- Отрицательные физические параметры запрещены.
- Источник истины — JSON; ScriptableObject в `Generated` создаются импортёром.

## TankDefinition

```json
{
  "schemaVersion": 1,
  "id": "tiger_ii",
  "displayName": "Tiger II",
  "vehicleClass": "HeavyTank",
  "nation": "Germany",
  "availableInGame": false,
  "maxHitPoints": 1600,
  "weaponId": "kwk_43_l71",
  "mobility": {
    "maxForwardSpeed": 1.9,
    "maxReverseSpeed": 0.85,
    "acceleration": 0.16,
    "groundResistance": 0.85,
    "braking": 1.25,
    "hullTurnSpeed": 18.0,
    "turretTurnSpeed": 18.0
  },
  "armor": {
    "front": 150.0,
    "left": 80.0,
    "right": 80.0,
    "rear": 80.0
  },
  "vision": {
    "viewRange": 9.5,
    "stationaryConcealment": 0.08,
    "movementRevealPenalty": 0.04,
    "firingRevealPenalty": 0.20,
    "firingRevealDuration": 6.0,
    "guaranteedDetectionRange": 1.4
  }
}
```

Единицы:

- скорости движения — игровые единицы в секунду;
- ускорения — игровые единицы в секунду в квадрате;
- угловые скорости — градусы в секунду;
- броня — миллиметры;
- HP — целое положительное число.

Поля обзора:

- `viewRange` — положительная максимальная дальность обзора в игровых единицах;
- `stationaryConcealment` — базовая маскировка от `0` до `0,95`;
- `movementRevealPenalty` и `firingRevealPenalty` — вычитаемые из маскировки коэффициенты от `0` до `0,95`;
- `firingRevealDuration` — неотрицательная длительность штрафа после выстрела в секундах;
- `guaranteedDetectionRange` — положительная дистанция, не превышающая `viewRange`.

`vehicleClass` обязателен и принимает одно из пяти значений: `HeavyTank`, `MediumTank`, `LightTank`, `TankDestroyer` или `Artillery`. Класс определяет символ машины в мире и HUD, но сам по себе не меняет боевые характеристики.

`nation` обязателен и пока принимает `Germany` или `USSR`. `availableInGame` отделяет сохранённую в проекте технику от активного состава: недоступная машина может иметь данные, спрайт и префаб, но не попадает на полигон и запрещена в сценариях боя.

## WeaponDefinition

```json
{
  "schemaVersion": 1,
  "id": "kwk_43_l71",
  "displayName": "8.8 cm KwK 43 L/71",
  "reloadSeconds": 8.0,
  "shellId": "pzgr_39_43"
}
```

`reloadSeconds` должен быть больше нуля. `shellId` обязан ссылаться на существующий снаряд.

## ShellDefinition

```json
{
  "schemaVersion": 1,
  "id": "pzgr_39_43",
  "displayName": "PzGr 39/43",
  "damage": 300,
  "penetration": 203.0,
  "speed": 18.0,
  "radius": 0.06,
  "lifetimeSeconds": 3.0,
  "maximumRange": 45.0,
  "ricochetAngle": 70.0,
  "ricochetSpeedMultiplier": 0.7,
  "ricochetPenetrationMultiplier": 0.65,
  "maximumRicochets": 2
}
```

Ограничения:

- `damage` — целое число больше нуля;
- `penetration`, `speed`, `radius`, `lifetimeSeconds`, `maximumRange` — больше нуля;
- `ricochetAngle` — от `0` до `90` градусов включительно;
- множители после рикошета — больше нуля и не больше единицы;
- `maximumRicochets` — целое число от `0` до `8`.

Исторические ориентиры и перевод скорости из игровых единиц приведены в `Docs/TANK_REFERENCE.md`. HP, урон и визуальная скорость снаряда являются игровыми величинами и не должны выдаваться за паспортные характеристики.

## Ошибки, которые обязан находить валидатор

- синтаксически неверный JSON;
- отсутствующее обязательное поле;
- неподдерживаемая версия схемы;
- пустой или некорректный ID;
- повторяющийся ID;
- ссылка на отсутствующее оружие или снаряд;
- неизвестная нация;
- значение вне допустимого диапазона;
- нечисловые `NaN` и бесконечность после преобразования;
- файл неправильного типа в каталоге определений.
