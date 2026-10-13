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
  "tier": 8,
  "availableInGame": false,
  "maxHitPoints": 1600,
  "massTonnes": 69.8,
  "enginePowerHp": 700.0,
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
  "gunHandling": {
    "minimumDispersionDegrees": 0.0,
    "maximumDispersionDegrees": 2.0,
    "aimingTimeSeconds": 2.7,
    "movementDispersionDegrees": 1.1,
    "hullTraverseDispersionDegrees": 0.85,
    "turretTraverseDispersionDegrees": 0.3,
    "shotDispersionDegrees": 1.3
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
- масса — положительное число в тоннах и источник массы `Rigidbody2D`;
- мощность двигателя — положительное число в лошадиных силах;
- `tier` — игровой уровень техники World of Tanks, целое число от `1` до `10` включительно.

Поля сведения:

- `minimumDispersionDegrees` и `maximumDispersionDegrees` — минимальное и предельное угловое отклонение снаряда; максимум должен быть не меньше минимума;
- `aimingTimeSeconds` — время восстановления от максимального разброса до минимального при полной неподвижности;
- `movementDispersionDegrees` — штраф при движении с предельной скоростью;
- `hullTraverseDispersionDegrees` — штраф при повороте корпуса с предельной скоростью;
- `turretTraverseDispersionDegrees` — штраф вращения башни; для текущих машин он меньше штрафа корпуса;
- `shotDispersionDegrees` — мгновенное расширение круга после выстрела.

Все угловые значения задаются в градусах и не могут быть отрицательными. Нулевой минимальный разброс означает идеально точный полностью сведённый выстрел.

Поля обзора:

- `viewRange` — положительная максимальная дальность обзора в игровых единицах;
- `stationaryConcealment` — базовая маскировка от `0` до `0,95`;
- `movementRevealPenalty` и `firingRevealPenalty` — вычитаемые из маскировки коэффициенты от `0` до `0,95`;
- `firingRevealDuration` — неотрицательная длительность штрафа после выстрела в секундах;
- `guaranteedDetectionRange` — положительная дистанция, не превышающая `viewRange`.

`vehicleClass` обязателен и принимает одно из пяти значений: `HeavyTank`, `MediumTank`, `LightTank`, `TankDestroyer` или `Artillery`. Класс определяет символ машины в мире и HUD, но сам по себе не меняет боевые характеристики.

`nation` обязателен и пока принимает `Germany` или `USSR`. `availableInGame` отделяет сохранённую в проекте технику от активного состава: недоступная машина может иметь данные, спрайт и префаб, но не попадает на полигон и запрещена в сценариях боя.

`tier` относится к представлению конкретной машины в World of Tanks, а не к исторической классификации. Для новой техники он берётся с официальной страницы Tankopedia или другого официального материала WoT. Значение вне диапазона 1–10 отклоняется; уровень XI нельзя молча преобразовывать в X.

### Аудиопрофиль активной машины

```json
"audio": {
  "engineClipId": "engine_eng_gaz_aa_pc",
  "tracksClipId": "tracks_trds_med_npc",
  "shotClipId": "gun_wpn_small_pc",
  "reloadClipId": "reload_gun_rld_20_45mm",
  "turretClipId": "turret_tur_man_trav_04",
  "destructionClipId": "destruction_expl_crew_death_s_npc",
  "engineVolume": 0.42,
  "tracksVolume": 0.2,
  "shotVolume": 0.86,
  "reloadVolume": 0.36,
  "turretVolume": 0.18,
  "destructionVolume": 0.78,
  "enginePitch": 0.94,
  "tracksPitch": 0.94,
  "shotPitch": 0.94,
  "reloadPitch": 0.94,
  "turretPitch": 0.96,
  "destructionPitch": 0.96
}
```

Для `availableInGame: true` профиль обязателен. Clip ID ссылаются на локально воспроизводимые результаты `WoTAudioPipeline`, а не на Unity GUID; поэтому общий двигатель, гусеницы, орудие, механизм перезарядки и общий эффект уничтожения переиспользуются без копий и без битых ссылок при отсутствии локальных WoT-материалов. Volume ограничен диапазоном 0–1, pitch — 0,25–3. Скрытая техника может не иметь аудиопрофиля до возвращения в активный состав.

Роли клипов: `engine` — зацикленный двигатель; `tracks` — зацикленные гусеницы; `shot` — выстрел; `reload` — механизм перезарядки; `turret` — зацикленный механизм поворота башни; `destruction` — однократный эффект уничтожения машины.

Если блок `audio` присутствует, валидатор требует непустой идентификатор для каждой из шести ролей и числовые значения в допустимых диапазонах: частично заполненный блок отклоняет весь набор определений. Отсутствие блока допустимо только для недоступной техники.

### Аудиопрофиль попадания снаряда

```json
"audio": {
  "penetrationClipId": "impact_small_pierced",
  "blockedClipId": "impact_small_blocked",
  "ricochetClipId": "impact_small_ricochet",
  "penetrationVolume": 0.28,
  "blockedVolume": 0.2,
  "ricochetVolume": 0.2,
  "pitch": 0.96
}
```

Профиль привязан к фактическому результату расчёта брони, а не к визуальному эффекту: `penetration` проигрывается при пробитии, `blocked` — при непробитии и при попадании в разрушаемый объект, `ricochet` — при рикошете. Все три идентификатора обязательны при наличии блока, поскольку варианты одного события не подменяют друг друга; общий `pitch` и отдельные громкости позволяют различать калибры без отдельных физических файлов.

### Тишина в автоматическом прогоне

В batchmode звук не воспроизводится вообще: `ResourcesTankAudioClipProvider` возвращает `null` вместо клипа, поэтому не создаются ни зацикленные источники машины, ни разовые источники попаданий. Причина — автоматические наборы запускаются без графики и звука, а локальные клипы WoT на сборочной машине обычно отсутствуют. Симуляция при этом не меняется: все звуковые пути уже рассчитаны на отсутствующий клип, поэтому бой, стрельба и урон идут как обычно, а `TankAudioController` и `CombatAudioEmitter` просто молчат. Интерактивный запуск в редакторе и в сборке звук сохраняет. Гарантию проверяет `BatchmodeAudioSilencePlayModeTests`.


## WeaponDefinition

```json
{
  "schemaVersion": 1,
  "id": "kwk_43_l71",
  "displayName": "8.8 cm KwK 43 L/71",
  "caliberMm": 88,
  "reloadSeconds": 8.0,
  "magazineSize": 1,
  "shotIntervalSeconds": 0.0,
  "shellId": "pzgr_39_43"
}
```

`reloadSeconds` должен быть больше нуля, `magazineSize` — от 1 до 100, `shotIntervalSeconds` — неотрицательным. Для обычного орудия используются магазин 1 и интервал 0; для автоматического орудия перезарядка начинается после расходования магазина. `shellId` обязан ссылаться на существующий снаряд.

`caliberMm` — диаметр канала ствола в миллиметрах, положительное число. Он переносится из той же топовой конфигурации World of Tanks, что и остальные параметры орудия, и служит единственной причиной, по которой 45-мм и 88-мм выстрелы выглядят по-разному: визуальный слой выбирает полосу эффекта по калибру, а не по отдельному визуальному профилю машины. Значение обязано быть положительным, иначе выстрел молча получил бы самую слабую полосу.

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
  "maximumRicochets": 2,
  "audio": {
    "penetrationClipId": "impact_small_pierced",
    "blockedClipId": "impact_small_blocked",
    "ricochetClipId": "impact_small_ricochet",
    "penetrationVolume": 0.3,
    "blockedVolume": 0.22,
    "ricochetVolume": 0.22,
    "pitch": 0.92
  }
}
```

Ограничения:

- `damage` — целое число больше нуля;
- `penetration`, `speed`, `radius`, `lifetimeSeconds`, `maximumRange` — больше нуля;
- `ricochetAngle` — от `0` до `90` градусов включительно;
- множители после рикошета — больше нуля и не больше единицы;
- `maximumRicochets` — целое число от `0` до `8`;
- при наличии блока `audio` — непустые `penetrationClipId`, `blockedClipId`, `ricochetClipId` и громкости в диапазоне 0–1.

Исторические ориентиры и перевод скорости из игровых единиц приведены в `Docs/TANK_REFERENCE.md`. HP, урон и визуальная скорость снаряда являются игровыми величинами и не должны выдаваться за паспортные характеристики.

Полная сравнительная таблица актуальных игровых значений автоматически создаётся в `Docs/TANK_BALANCE.md`. Её обновляет тот же импортёр, а Edit Mode тест запрещает незаметное расхождение документа с JSON.

## Профиль визуальных эффектов

`Assets/Game/Resources/CombatVfxProfiles.json` — единственный редактируемый источник визуальных эффектов боя. Он описывает не машины, а полосы: по калибру, массе, оставшемуся здоровью, результату попадания и типу поверхности. Ни один танк, снаряд или префаб не хранит визуальных параметров, поэтому добавление машины не требует правки этого файла, а изменение арт-направления не требует правки машин.

| Таблица | Ключ | Обязательные поля |
|---|---|---|
| `weaponProfiles` | `maxCaliberMm` по возрастанию, последняя полоса открыта | `flashScale`, `flashSeconds`, `smokeScale`, `smokeSeconds`, `smokeBurst`, `recoilDistance`, `lightIntensity`, `lightRadius`, `lightSeconds` |
| `movementProfiles` | `maxMassTonnes` по возрастанию | `dustOpacity`, `dustSizeFraction`, `dustRate`, `dustSpeed`, `exhaustIdleRate`, `exhaustLoadedRate`, `markOpacity`, `markSpacingFraction`, `markLengthFraction`, `markWidthFraction`, `markFadeSeconds`, `markLimit`, `sprayRate` |
| `damageProfiles` | `minHealthFraction` по убыванию; последняя полоса обязана начинаться с нуля и нести `wreck: true` | `smokeRate`, `smokeOpacity`, `smokeSizeFraction`, `smokeRise`, `fire` |
| `impactProfiles` | `outcome`: `penetrated`, `blocked`, `ricocheted`, `ground` | `flashScale`, `sparkCount`, `sparkSpeed`, `dustScale`, `debrisCount`, `seconds` |
| `surfaceProfiles` | `terrain`: имя значения `TerrainKind`, каждое ровно один раз | `dustTint`, `dustRateMultiplier`, `markOpacityMultiplier`, `splash` |
| `limits` | — | `maxPooledInstances`, `qualityScale` |

Единицы и смысл:

- дроби (`*Fraction`) заданы относительно габаритов корпуса машины: ширина и длина отпечатка — от ширины и длины корпуса, размер клуба пыли и дыма — от ширины корпуса. Один и тот же профиль даёт мелкому танку мелкий эффект, а тяжёлому — крупный, без отдельных чисел на машину;
- `markSpacingFraction` задаёт шаг между отпечатками как долю длины корпуса, `markLimit` — сколько отпечатков одна машина держит на земле одновременно;
- `dustSpeed` и `smokeRise` — множители к скорости, запечённой в сгенерированный префаб;
- `sparkSpeed`, `seconds`, `flashSeconds`, `smokeSeconds` попадают в префаб эффекта при генерации, а не применяются в бою;
- цвета задаются объектом `{ "r": …, "g": …, "b": …, "a": … }` в диапазоне 0–1;
- `splash: true` отключает пыль и отпечаток и включает брызги.

Обязательные инварианты, которые проверяет `CombatVfxCatalog.Validate`, команда `Local Tanks > Data/Validate Combat Vfx Profiles`, генератор ассетов и Edit Mode тест:

- ID уникален во всём файле, а не внутри таблицы: одна строка `blocked` не может одновременно означать непробитие и непроходимую клетку;
- каждая `TerrainKind` и каждый результат попадания описаны ровно один раз;
- `flashScale` не падает с ростом калибра, `dustRate` — с ростом массы, `smokeRate` — с падением здоровья: полоса, нарушающая порядок, делает свою таблицу бессмысленной;
- нижняя полоса повреждения начинается с нуля, иначе машина на грани уничтожения осталась бы вообще без дыма;
- ровно одна полоса повреждения несёт `wreck: true` и стоит последней;
- `maxPooledInstances` ограничивает число одновременных временных эффектов, `qualityScale` — единственное место, где вся библиотека разом делается дешевле.

Отсутствие файла, пустая таблица или отсутствие сгенерированных префабов не являются ошибкой боя: эффекты просто не рисуются, а симуляция остаётся неизменной. Отсутствие положительного `caliberMm` у орудия или положительной массы у машины ошибкой является, потому что молчаливая подстановка первой полосы выглядела бы как дефект визуального слоя.

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
