use std::collections::HashSet;

type Id = [u8; 16];

/// Validate the IDs and session references in one journal snapshot.
/// Returns 0 on success, 1 for invalid session IDs, 2 for invalid catch links,
/// and -1 for an unsupported buffer.
///
/// # Safety
/// A nonempty session buffer contains exactly `session_count` 16-byte IDs.
/// A nonempty catch buffer contains `catch_count` pairs of 16-byte IDs, in
/// catch-ID then session-ID order. The caller keeps the buffers valid for the call.
#[no_mangle]
pub unsafe extern "C" fn journal_validate_links(
    session_ids: *const u8,
    session_count: i32,
    catch_links: *const u8,
    catch_count: i32,
) -> i32 {
    if !(0..=1000).contains(&session_count)
        || !(0..=20000).contains(&catch_count)
        || (session_count != 0 && session_ids.is_null())
        || (catch_count != 0 && catch_links.is_null())
    {
        return -1;
    }

    let sessions = if session_count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(session_ids.cast::<Id>(), session_count as usize) }
    };
    let catches = if catch_count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(catch_links.cast::<[Id; 2]>(), catch_count as usize) }
    };
    let empty = [0; 16];
    let mut ids = HashSet::with_capacity(sessions.len());
    for id in sessions {
        if *id == empty || !ids.insert(id) {
            return 1;
        }
    }

    let mut seen = HashSet::with_capacity(catches.len());
    for [id, session] in catches {
        if *id == empty || !seen.insert(id) || !ids.contains(session) {
            return 2;
        }
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    fn validate(sessions: &[Id], catches: &[[Id; 2]]) -> i32 {
        unsafe {
            journal_validate_links(
                sessions.as_ptr().cast(),
                sessions.len() as i32,
                catches.as_ptr().cast(),
                catches.len() as i32,
            )
        }
    }

    #[test]
    fn preserves_key_and_reference_rules() {
        let (empty, session, another_session, catch) = ([0; 16], [0x11; 16], [0x22; 16], [0x33; 16]);
        assert_eq!(validate(&[], &[]), 0);
        assert_eq!(validate(&[session], &[[catch, session]]), 0);
        assert_eq!(validate(&[empty], &[]), 1);
        assert_eq!(validate(&[session, session], &[[empty, empty]]), 1);
        assert_eq!(validate(&[session], &[[empty, session]]), 2);
        assert_eq!(validate(&[session], &[[catch, another_session]]), 2);
        assert_eq!(validate(&[session], &[[catch, session]; 2]), 2);
    }

    #[test]
    fn rejects_unsupported_buffer_sizes() {
        let null = std::ptr::null();
        let input = [0u8; 32];
        unsafe {
            assert_eq!(journal_validate_links(null, 0, null, 0), 0);
            assert_eq!(journal_validate_links(null, 1, null, 0), -1);
            assert_eq!(journal_validate_links(null, 0, null, 1), -1);
            assert_eq!(journal_validate_links(input.as_ptr(), -1, null, 0), -1);
            assert_eq!(journal_validate_links(input.as_ptr(), 1001, null, 0), -1);
            assert_eq!(journal_validate_links(input.as_ptr(), 0, input.as_ptr(), 20001), -1);
        }
    }
}
