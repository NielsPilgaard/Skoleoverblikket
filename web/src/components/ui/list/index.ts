// Lists (task 67, part 1):
// - PersonList: people (staff, students, parents). Two-line rows that fold out.
// - DataTable: rows users compare across columns. Table on laptop, cards on phone.
// Both share ListToolbar (search, filter chips, count, sort) via useListControls.
// Wide number grids (UVM-timetal, fravær per week) need a sticky first column instead.

export { type Column, DataTable } from './DataTable'
export { type Person, PersonList } from './PersonList'
export type { Badge, RowAction } from './parts'
export type { ListFilter, ListSort } from './useListControls'
