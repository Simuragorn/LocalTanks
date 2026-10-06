# Схема игровых данных

Статус: целевая схема Sprint 002. До реализации импортёра файлы являются спецификацией.

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
  "maxHitPoints": 1600,
  "weaponId": "kwk_43_l71",
  "mobility": {
    "maxForwardSpeed": 3.0,
    "maxReverseSpeed": 0.85,
    "acceleration": 0.3,
    "groundResistance": 1.35,
    "braking": 1.8,
    "hullTurnSpeed": 24.0,
    "turretTurnSpeed": 18.0
  },
  "armor": {
    "front": 150.0,
    "left": 80.0,
    "right": 80.0,
    "rear": 80.0
  }
}
```

Единицы:

- скорости движения — игровые единицы в секунду;
- ускорения — игровые единицы в секунду в квадрате;
- угловые скорости — градусы в секунду;
- броня — миллиметры;
- HP — целое положительное число.

## WeaponDefinition

```json
{
  "schemaVersion": 1,
  "id": "kwk_43_l71",
  "displayName": "8.8 cm KwK 43 L/71",
  "reloadSeconds": 0.8,
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
  "damage": 320,
  "penetration": 225.0,
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

## Ошибки, которые обязан находить валидатор

- синтаксически неверный JSON;
- отсутствующее обязательное поле;
- неподдерживаемая версия схемы;
- пустой или некорректный ID;
- повторяющийся ID;
- ссылка на отсутствующее оружие или снаряд;
- значение вне допустимого диапазона;
- нечисловые `NaN` и бесконечность после преобразования;
- файл неправильного типа в каталоге определений.

