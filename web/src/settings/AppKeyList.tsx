import { useId, useState, type KeyboardEvent } from 'react';
import type { AppDto } from '../api/client.ts';
import { addKey } from './settingsForm.ts';

interface AppKeyListProps {
  /** The list's name, e.g. "Excluded apps"; labels the field and the remove buttons. */
  label: string;
  /** One sentence on what being in the list does. */
  description: string;
  keys: string[];
  onChange: (keys: string[]) => void;
  /** Apps to suggest while typing, from recent weeks. */
  suggestions: AppDto[];
  /** Errors for this list from the form or the API. */
  errors?: string[] | undefined;
}

/** A list of app keys: each shown with a remove button, and a field to add one. */
export function AppKeyList({
  label,
  description,
  keys,
  onChange,
  suggestions,
  errors,
}: AppKeyListProps) {
  const id = useId();
  const [draft, setDraft] = useState('');
  const names = new Map(suggestions.map((a) => [a.key.toLowerCase(), a.name]));

  const add = () => {
    onChange(addKey(keys, draft));
    setDraft('');
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    // Enter adds the app instead of submitting the whole form.
    if (event.key === 'Enter') {
      event.preventDefault();
      add();
    }
  };

  return (
    <fieldset className="settings__field" aria-describedby={`${id}-description`}>
      <legend>{label}</legend>
      <p id={`${id}-description`} className="settings__hint">
        {description}
      </p>
      {keys.length === 0 ? (
        <p className="settings__none">None.</p>
      ) : (
        <ul className="app-keys" aria-label={label}>
          {keys.map((key) => (
            <li key={key} className="app-keys__item">
              <span>
                {key}
                {names.has(key.toLowerCase()) && names.get(key.toLowerCase()) !== key && (
                  <span className="app-keys__name"> ({names.get(key.toLowerCase())})</span>
                )}
              </span>
              <button
                type="button"
                className="app-keys__remove"
                aria-label={`Remove ${key} from ${label.toLowerCase()}`}
                onClick={() => onChange(keys.filter((k) => k !== key))}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="settings__add">
        <input
          id={`${id}-input`}
          type="text"
          value={draft}
          list={`${id}-apps`}
          aria-label={`Add to ${label.toLowerCase()}`}
          placeholder="App key, e.g. vlc"
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={onKeyDown}
        />
        <datalist id={`${id}-apps`}>
          {suggestions.map((app) => (
            <option key={app.key} value={app.key}>
              {app.name}
            </option>
          ))}
        </datalist>
        <button
          type="button"
          className="settings__button"
          onClick={add}
          disabled={draft.trim() === ''}
        >
          Add
        </button>
      </div>
      <FieldErrorList errors={errors} />
    </fieldset>
  );
}

/** The errors of one field, read out when they appear. */
export function FieldErrorList({ errors }: { errors?: string[] | undefined }) {
  if (!errors || errors.length === 0) {
    return null;
  }
  return (
    <ul className="settings__errors" role="alert">
      {errors.map((error) => (
        <li key={error}>{error}</li>
      ))}
    </ul>
  );
}
