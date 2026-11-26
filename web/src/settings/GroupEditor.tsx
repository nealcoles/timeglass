import { FieldErrorList } from './AppKeyList.tsx';
import type { GroupRow } from './settingsForm.ts';

export function GroupEditor({
  rows,
  onChange,
  errors,
}: {
  rows: GroupRow[];
  onChange: (rows: GroupRow[]) => void;
  errors: string[] | undefined;
}) {
  function move(index: number, direction: number) {
    const next = [...rows];
    const target = index + direction;
    const current = next[index];
    const other = next[target];
    if (!current || !other) return;
    next[index] = other;
    next[target] = current;
    onChange(next);
  }
  return (
    <fieldset className="settings__field">
      <legend>Keyword groups</legend>
      <p className="settings__hint">
        Match any keyword against application names or recorded window titles, ignoring case. The
        first matching group wins; everything else is Ungrouped. Older time retained without titles
        can only match application names. Tracked away time is included automatically. Separate
        keywords with commas.
      </p>
      {rows.length === 0 ? (
        <p className="settings__none">No keyword groups yet.</p>
      ) : (
        <ol className="keyword-groups" aria-label="Keyword groups in priority order">
          {rows.map((row, i) => (
            <li className="keyword-groups__item" key={i}>
              <div className="keyword-groups__header">
                <span className="keyword-groups__priority">Priority {i + 1}</span>
                <div className="keyword-groups__actions">
                  <button
                    type="button"
                    className="settings__button"
                    disabled={i === 0}
                    onClick={() => move(i, -1)}
                    aria-label={`Move group ${i + 1} up`}
                  >
                    Move up
                  </button>
                  <button
                    type="button"
                    className="settings__button"
                    disabled={i === rows.length - 1}
                    onClick={() => move(i, 1)}
                    aria-label={`Move group ${i + 1} down`}
                  >
                    Move down
                  </button>
                  <button
                    type="button"
                    className="settings__button"
                    onClick={() => onChange(rows.filter((_, n) => n !== i))}
                    aria-label={`Remove group ${i + 1}`}
                  >
                    Remove
                  </button>
                </div>
              </div>
              <div className="keyword-groups__fields">
                <label>
                  Group name
                  <input
                    type="text"
                    aria-label={`Group ${i + 1} name`}
                    placeholder="e.g. Investigator"
                    value={row.name}
                    onChange={(e) =>
                      onChange(rows.map((r, n) => (n === i ? { ...r, name: e.target.value } : r)))
                    }
                  />
                </label>
                <label>
                  Keywords
                  <input
                    type="text"
                    aria-label={`Keywords, group ${i + 1}`}
                    placeholder="e.g. investigator, research"
                    value={row.keywords}
                    onChange={(e) =>
                      onChange(
                        rows.map((r, n) => (n === i ? { ...r, keywords: e.target.value } : r)),
                      )
                    }
                  />
                </label>
              </div>
            </li>
          ))}
        </ol>
      )}
      <button
        type="button"
        className="settings__button"
        onClick={() => onChange([...rows, { name: '', keywords: '' }])}
      >
        Add group
      </button>
      <FieldErrorList errors={errors} />
    </fieldset>
  );
}
