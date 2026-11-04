# Как расширять прототип: рецепты

Короткие инструкции для типовых задач. Всё описано в коде, поэтому ассеты и префабы не обязательны.
Префабы можно подключать постепенно. Цифры баланса геймдизайнер правит в ассетах — см. [BALANCE](BALANCE.md).

## Добавить постройку
1. В `StructureKind` ([Building/BuildingDefs.cs](../Assets/Echo7/Scripts/Building/BuildingDefs.cs)) добавьте значение
   **в конец** перечисления — иначе сломаются старые сохранения.
2. В `BuildingCatalog.CodeDefaults()` добавьте `new BuildingDef { ... }`: размер в клетках (1 клетка = 2 м), стоимость, прочность,
   время стройки, энергию, допуск (`RequiredFeature`, `MinRank` или `RequiredFlag`), класс `Component` и метод модели `Model`.
   Это значения по умолчанию. Дальше дизайнер выбирает **Эхо-7 → Баланс → Создать или дополнить ассеты**: запись допишется в `Buildings`,
   и цифры можно настраивать там. Если это турель — добавьте тип в `TurretProfile.TurretKinds` и профиль в `TurretProfile.CodeDefault()`.
3. Модель — метод в `StructureModels`. Начало координат — центр основания. Подвижные части кладите в узлы с именем
   на `#` (например, `#head`) и находите их в `OnBuilt()` через `Model.Find("#head")`.
4. Поведение — наследник `Structure`:
   - `OnCreated()` вызывается при установке чертежа;
   - `OnBuilt()` — когда достроено;
   - `Update()` — работа (не забудьте `base.Update()`);
   - `OnRemoved()` — снос;
   - `StatusText()` — строка для инспектора;
   - `NeedsService` / `Service()` — если постройку должны обслуживать дроны.

Чтобы использовать готовый префаб, внутри `Model` вызовите `Object.Instantiate(prefab, mb.Root)`. Префаб должен
содержать те же узлы `#…`, что ищет скрипт.

## Добавить существо
1. Значение в `EnemyKind` (в конец), описание в `EnemyArchetype.CodeDefaults()`: здоровье, броня, скорость, урон,
   цена в бюджете волны (`Cost`), `MinDay` и `MinRank`. Затем «Дополнить ассеты» — запись появится в `Enemies`.
2. Модель — метод `Build…` в `CreatureRig` (ноги через `AddLeg`, у головы узел `_head` для выпада).
3. Поведение настраивается в `Enemy.Think()` (выбор цели) и `AssaultBehaviour()`. Директор подхватит новый тип
   автоматически; вес в составе волны задаётся в `EnemyOnslaughtDirector.Compose()`.

## Добавить задачу Центра
Без программиста: в ассете `Tasks` добавьте шаблон в список `pool` (случайные) или `starter` (по порядку).
Поля: тип, цель и её рост с рангом, срок, награда, минимальный ранг, вес.
Значения по умолчанию в коде — `TaskBalance.ResetToCode()`. Если нужен новый тип прогресса,
добавьте значение в `TaskType` и подпишитесь на нужное событие `GameEvents` в `CenterTaskBoard.InitEventsOnly()`.

## Добавить диалог
```csharp
var node = new DialogueNode { Speaker = "Лейтенант Орлова", Portrait = "О", Text = "..." };
node.Add("Выдать патроны (−20)", () => Game.Bank.Spend(ResourceType.Ammo, 20), null,
         () => Game.Bank.Has(ResourceType.Ammo, 20) ? null : "Нет патронов");
node.Add("Уйти");
Game.Dialogue.Start(node);
```
Ветвление — через параметр `next` (функция, возвращающая следующий узел). Условие недоступности — через
`unavailable` (возвращает причину или `null`).

## Новый визит высокого чина
В ассете `Vips` добавьте запись в `visits`: должность, фамилия, символ портрета, текст миссии, запросы с ценами
(«лучшее» и «похуже», рост с рангом), награда (флаг, сообщение, кредиты), минимальный ранг и вес.
Новый флаг награды начинает действовать там, где код его проверяет через `Game.Entry.HasFlag`
(например, `RequiredFlag` у постройки в ассете `Buildings`).

## Новый параметр баланса
См. раздел «Для программиста» в [BALANCE](BALANCE.md): поле в записи, строки в `From`/`ApplyTo`, проверка в `Validate`.

## Новый допуск по рангу
1. Значение в `Feature` и строка в `FeatureRanks` ([Progression/PlayerRankManager.cs](../Assets/Echo7/Scripts/Progression/PlayerRankManager.cs)).
2. Используйте его в `BuildingDef.RequiredFeature`, `UiManager.RequiredFeature(window)`, `GlobalRegion.RequiredFeature`
   или в условии диалога: `() => Game.Rank.LockReason(Feature.X)`.

## Сценарная волна
```csharp
Game.Onslaught.Schedule(WaveTrigger.Scripted, budget: 40, delaySeconds: 30f, "Атака на рудник",
                        direction: new Vector3(1, 0, 0));
```

## Свои звуки
```csharp
Game.Sound.Override(Sfx.Rifle, myClip);   // например, в Echo7Entry после Game.Sound.Init()
```

## Флаги сюжета
`Game.Entry.SetFlag("id")`, `HasFlag`, `ClearFlag`. Флаги сохраняются. Флаг `free_<StructureKind>` делает следующую
постройку этого типа бесплатной (так работает орбитальная доставка).

## Учебное задание пролога
В `TutorialDirector.BuildSteps()` добавьте `new TutorialStep { … }` в нужное место списка:
- `Title`, `Briefing` (rich text), `Objective`, `Hint`;
- `Progress` — функция 0..1. Счётчик `_stepCounter` удобно пополнять из обработчиков событий, сверяясь со `StepIndex`;
- `ProgressText` — например, «4/6»;
- `Marker` — точка в мире или `null`;
- `Reveal` — какие `HudPart` открыть, `Unlock` — какие постройки разрешить;
- `HighlightWindow` / `HighlightKind` / `HighlightAbility` — что подсветить;
- `Enter` — действие при начале шага (запланировать волну, прислать капсулу, перевести часы).

Если вставляете шаг до «Первой ночи», сдвиньте константу `NightStep` и номера шагов в `WindowRevealed` и `OnStructureCompleted`.

## Карточка «НОВОЕ»
`Game.Tutorial.ShowTip("id", "Заголовок", "Текст")` из любой системы. Карточка покажется один раз за партию:
флаг `tip_id` сохраняется. Для механик, у которых нет события, добавьте проверку в `TutorialDirector.PollTips()`.

## Проверка компиляции без Unity
Скрипты не используют пакеты: только встроенные модули движка и (опционально) Input System. Их можно проверить
`dotnet build`-проектом, который ссылается на сборки `UnityEngine.*Module.dll` (так проект проверялся при создании).
