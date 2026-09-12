# Stash Master — An Opinionated Stash Manager

**English** | [简体中文](README.zh-CN.md)

- **One-click organizing**: fold, merge stacks, put items away, and sort.
- **Fill your containers**: fit more by rearranging their contents and putting
  awkward gaps to use.
- **Compact layouts**: keep similar items together, instead of splitting them
  between the top and bottom of your stash.

## The frustration

Tired of sorting failing for no apparent reason?

Five weapons already fit in the case, with two empty rows at the bottom.
Click sort, and you get an error:

![Two rows remain empty in the weapons case, yet sorting fails][sort-failure]

Everything fits, but the sorter cannot put it back together. Weapons placed early
can block the space a longer weapon needs later. Stash Master plans positions and
orientations together, moving earlier items out of the way when it needs to.

We also tested three separate weapons case combinations. Native sorting and
Stash Management Helper each failed on some of them; Stash Master found a complete
layout for all three. See the [illustrated cases, sizes, and results][comparison]
(in Chinese).

![Three weapons case inputs and results for Native, SMH, and Stash Master][case-inputs]

✓ Sorting completed; × sorting failed and the original layout was retained.
Stash Management Helper used the settings listed below.

## How it compares

### Armor at the bottom, without the gaps

Native sorting puts armor at the bottom, leaving gaps scattered between pieces
of gear and wasting space. With Stash Master, armor stays at the bottom,
but the layout is much tighter:

| Native | Stash Master |
| --- | --- |
| ![Native: gaps between pieces of gear][armor-native] | ![Stash Master][armor-sm] |

### Stash Management Helper's default sorting

Stash Management Helper puts armor at the top by default and does a good job.
Still, a keychain, magazines, and loose ammo find their way into the gaps
between pieces of gear:

With `Backpacks > Armor > Rigs`, Stash Master produces a similar equipment layout,
without keys, magazines, or loose ammo mixed in between:

| Stash Management Helper | Stash Master |
| --- | --- |
| ![SMH: small items in red boxes][armor-smh] | ![Stash Master][armor-sm-custom] |

### Measurements from the same stash

The same messy stash, after folding, merging, and collection, with the same items
sorted three ways:

| Layout result | Native | Stash Management Helper | Stash Master (ours) |
| --- | ---: | ---: | ---: |
| Stash rows used | 62 | **61** | **61** |
| Sum of category row spans | 106 | 212 | **59** |

Lower is better for both measures. A category's span is its bottommost occupied
row minus its topmost occupied row, summed across parent categories.
Pinned and Locked items stay put and are excluded from both measures, so the row
count does not mean that everything below it is empty.

Stash Management Helper used capacity-first, then area-first sorting, with type
sorting disabled. Native sorting and Stash Management Helper computed their
layouts faster in this comparison; ours spent more time on a compact arrangement.
[Full measurements, timings, and settings][timings] (in Chinese).

## Start organizing

Tag a **Money case** with `@o`, then click **the stash sort button** and
accept the game's confirmation. Loose money in your stash will go into the case.
Clicking a container's own sort button organizes the items inside that container.
Folding, stack merging, collection, and sorting run in order, with a notification
when they finish.

### Tag rules

`@o` collects anything the container can hold. Add a condition to limit what goes in.

| Container | Tag | What it collects |
| --- | --- | --- |
| Money case | `@o` | Loose money |
| Dogtag case | `@o#1` | Collect dogtags **1st**, before the junk box grabs them |
| Ammo case | `@o n:5.45;` | Only 5.45 ammo |
| Ammo case | `@o n:5.45 \|\| n:5.56;` | 5.45 or 5.56 ammo |
| Items case | `@o magazines && n:5.45;` | Only 5.45 magazines |
| Lucky Scav Junk box | `@o !n:dogtag;` | Accepted items, excluding dogtags |

```text
@o[#priority] [condition;]
```

Bracketed parts are optional; omit the brackets in your tag.

- **Names and categories**: `n:5.45` collects items with `5.45` in their name or
  short name. Without `n:`, use a full category name from the game's handbook;
  this includes everything in its subcategories. Use your current game language.
  Names and categories are case-insensitive.
- **Priority**: put a whole number directly after `@o#`.
  This sets the collection order across all containers. `#1` runs before `#2`;
  rules without a number run last. Ties have no guaranteed order.
  Items that do not fit can still go to containers with later rules.
- **Combining conditions**: `&&` means "and", `||` means "or", and `!` excludes
  items matching the condition immediately after it. When you use both `&&` and
  `||`, `&&` is checked first. Parentheses cannot change that order.
- **Spaces and semicolons**: use lowercase `@o` and `n:`. Leave a space between
  `@o` or `@o#1` and the condition, then end the condition with `;`.
  For `@o` or `@o#1` on its own, the semicolon is optional.

One tag can have several rules: `@o#1 n:5.45; @o#5` on an Ammo case collects
5.45 ammo at priority 1, then other ammo at priority 5.
A container name can go before `@o`; it does not affect the rules.
When you save a tag, a message explains each rule. If any rule has an error,
the message points it out, and the whole tag stays inactive until you fix it.

### Type order

Enabled by default, using the same item types as SMH. After your first sort, open
`BepInEx/config/com.chouun.stashmaster.category-order.json`
and arrange `itemTypeOrder` in your preferred order.
New configurations use Chinese type names when the game is in Chinese,
and English names for other languages.
For example, put armor, rigs, and weapons first:

```json
{
  "enabled": true,
  "itemTypeOrder": ["Armor", "Rigs", "Weapons"]
}
```

Omitted types follow the listed ones. Save and sort again; **no restart is needed**.
Chinese and English names can be mixed. Existing configurations need no changes,
even when you switch the game language.

<details>
<summary>Recommended order (default configuration)</summary>

```json
[
  "Containers", "Headsets", "Headgear", "NightAndThermalVision", "HeadgearArmor",
  "Eyewear", "Armor", "Rigs", "BallisticPlates", "Backpacks", "Weapons",
  "Magazines", "Ammo", "Grenades", "Meds", "Food", "Drink", "Facecovers",
  "Armband", "Melee", "Mods", "RepairKits", "SpecialEquipment", "Barter",
  "Keys", "Money", "Info"
]
```

</details>

### Organization rules and limits

- **Containers can be nested.** A case inside another case can still collect
  items using its tag. Stacks are merged and items rearranged in the container
  you are sorting and any containers inside it with rules.
  In containers without rules, items are only folded, not merged or moved.
- **Use the game's Pin and Lock.** Pinned and Locked items stay in place.
  Pinned stacks can still be topped up. Locked items are not merged,
  and nothing inside a Locked container is organized.
- **Top up stacks before finding empty slots.** Half stacks in different cases
  can be combined in one place. Even a case with no empty slots can take more
  items if an existing stack is not full.
- **A container keeps its existing items.** They can be rearranged to make room;
  anything that still does not fit is tried in other matching containers.
  Tags do not override container restrictions: an ammo case still cannot hold a gun.

## Compatibility

- **[UI Fixes][uifixes]**: works alongside Stash Master. We handle sorting and
  merging together, so you can leave its "Combine Stacks Before Sorting" enabled.
  Its other features, settings, and FiR stack mixing remain in effect.
- **[Stash Management Helper][smh]**: not fully compatible yet. Its right-click
  value/weight actions also trigger our organizing workflow and may leave its
  temporary sort settings unrestored.

Stash Master provides its own folding and merging; neither mod is required.

## Inspiration and thanks

I used and benefited from these mods for a long time. Thank you to their authors
and maintainers:

- [IOF: Inventory Organizing Feature][iof]: tag rules and recursive collection.
- [Stash Management Helper][smh]: custom sorting, automatic folding and merging.
- [UI Fixes][uifixes]: inventory quality of life and merging stacks before sorting.

This project started as an attempt to refactor IOF and somehow grew into a
complete organizing workflow. Its choices reflect my own taste, with plenty of
inspiration from the mods that came before it.

[Development notes](docs/development.md) · [Documentation](docs/index.md)
(in Chinese)

[sort-failure]: assets/images/failed-sort-example.png
[case-inputs]: assets/images/weapons-sort-comparison.svg
[armor-native]: assets/images/armor-sort-native.png
[armor-sm]: assets/images/armor-sort-stash-master.png
[armor-smh]: assets/images/armor-sort-smh.png
[armor-sm-custom]: assets/images/armor-sort-stash-master-custom.png
[comparison]: docs/reports/weapon-sort-boundaries.md
[timings]: docs/reports/sort-comparison.md
[iof]: https://sp-mod.com/mod/2960/iof-inventory-organizing-feature
[smh]: https://sp-mod.com/mod/1861/stash-management-helper
[uifixes]: https://sp-mod.com/mod/1342/ui-fixes
