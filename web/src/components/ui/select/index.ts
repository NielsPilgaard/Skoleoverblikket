// Which picker to use, chosen by situation (task 67, part 2):
//
// | Situation                               | Component        |
// |-----------------------------------------|------------------|
// | 2–4 short options                       | SegmentedControl |
// | The choice has a consequence to explain | RadioCards       |
// | 5–30 options                            | Select           |
// | Options the user repeats weekly         | RecentSelect     |
// | Over 30 options                         | ComboBox         |
// | Several at once                         | MultiSelect      |
// | Depends on availability                 | PersonPicker     |
// | Values in sequence (week, day, month)   | WeekStepper      |
//
// No swipe chips. Switching child uses SegmentedControl; switching class uses Select.
// Build the missing ones when a page needs them, and add them here.

export { RadioCards, type RadioCardOption } from './RadioCards'
export { SegmentedControl, type SegmentedOption } from './SegmentedControl'
export { Select, type SelectOption } from './Select'
export { WeekStepper } from './WeekStepper'
