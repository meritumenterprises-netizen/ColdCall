import { Injectable } from '@angular/core';
import { SearchProfile } from './api.service';

export interface SearchDraftState { drafts: SearchProfile[]; selectedSearchId: number | null; }

@Injectable({ providedIn: 'root' })
export class SearchDraftStore {
  load(userId: number): SearchDraftState {
    const empty: SearchDraftState = { drafts: [], selectedSearchId: null };
    if (!userId) { return empty; }
    try {
      const state = JSON.parse(sessionStorage.getItem(this.key(userId)) || 'null');
      if (!state || !Array.isArray(state.drafts)) { return empty; }
      return {
        drafts: state.drafts.filter(draft => draft && Number.isInteger(draft.id) && draft.id < 0
          && typeof draft.name === 'string' && Array.isArray(draft.pkdCodes)
          && draft.criteria && typeof draft.criteria === 'object'),
        selectedSearchId: Number.isInteger(state.selectedSearchId) ? state.selectedSearchId : null
      };
    } catch (_) { return empty; }
  }

  save(userId: number, drafts: SearchProfile[], selectedSearchId: number | null) {
    if (!userId) { return; }
    try {
      const unsaved = drafts.filter(draft => draft.id < 0);
      if (unsaved.length) {
        sessionStorage.setItem(this.key(userId), JSON.stringify({ drafts: unsaved, selectedSearchId }));
      } else { sessionStorage.removeItem(this.key(userId)); }
    } catch (_) { /* The dashboard remains usable when browser storage is unavailable. */ }
  }

  private key(userId: number) { return 'coldcall_search_drafts_' + userId; }
}
