export type GroupingView = 'apps' | 'groups';

export function ViewSelector({
  value,
  onChange,
}: {
  value: GroupingView;
  onChange: (value: GroupingView) => void;
}) {
  return (
    <div className="grouping-view" role="group" aria-label="View by">
      <span>View by</span>
      <button
        type="button"
        className="date-nav__view"
        aria-pressed={value === 'apps'}
        onClick={() => onChange('apps')}
      >
        Application
      </button>
      <button
        type="button"
        className="date-nav__view"
        aria-pressed={value === 'groups'}
        onClick={() => onChange('groups')}
      >
        Category
      </button>
    </div>
  );
}
