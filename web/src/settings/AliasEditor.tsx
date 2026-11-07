import { FieldErrorList } from './AppKeyList.tsx';
import type { AliasRow } from './settingsForm.ts';

interface AliasEditorProps {
  rows: AliasRow[];
  onChange: (rows: AliasRow[]) => void;
  errors?: string[] | undefined;
}

const emptyRow: AliasRow = { key: '', name: '', exes: '', titlePattern: '' };

/**
 * App renames and aliases: rename an app Timeglass knows (its key and a new name), or make a new
 * app from executables and, optionally, a window title pattern.
 */
export function AliasEditor({ rows, onChange, errors }: AliasEditorProps) {
  const update = (index: number, change: Partial<AliasRow>) =>
    onChange(rows.map((row, i) => (i === index ? { ...row, ...change } : row)));

  return (
    <fieldset className="settings__field">
      <legend>App names</legend>
      <p className="settings__hint">
        Rename an app by its key (shown in the address of its page), or add an app made of the
        executables you list. A title pattern (a regular expression) limits it to matching windows,
        such as one site in a browser.
      </p>
      {rows.length === 0 ? (
        <p className="settings__none">No renames or aliases.</p>
      ) : (
        <table className="aliases">
          <thead>
            <tr>
              <th scope="col">App key</th>
              <th scope="col">Name</th>
              <th scope="col">Executables</th>
              <th scope="col">Title pattern</th>
              <th scope="col">
                <span className="visually-hidden">Remove</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              // Rows have no identity of their own; the index is stable while typing.
              <tr key={i}>
                <td>
                  <input
                    type="text"
                    aria-label={`App key, row ${i + 1}`}
                    value={row.key}
                    spellCheck={false}
                    onChange={(e) => update(i, { key: e.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    aria-label={`Name, row ${i + 1}`}
                    value={row.name}
                    onChange={(e) => update(i, { name: e.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    aria-label={`Executables, row ${i + 1}`}
                    placeholder="game.exe, launcher.exe"
                    value={row.exes}
                    spellCheck={false}
                    onChange={(e) => update(i, { exes: e.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    aria-label={`Title pattern, row ${i + 1}`}
                    placeholder="Optional"
                    value={row.titlePattern}
                    spellCheck={false}
                    onChange={(e) => update(i, { titlePattern: e.target.value })}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className="app-keys__remove"
                    aria-label={`Remove row ${i + 1}`}
                    onClick={() => onChange(rows.filter((_, j) => j !== i))}
                  >
                    ×
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <button
        type="button"
        className="settings__button"
        onClick={() => onChange([...rows, emptyRow])}
      >
        Add a rename or alias
      </button>
      <FieldErrorList errors={errors} />
    </fieldset>
  );
}
