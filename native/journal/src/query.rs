type SessionId = [u8; 16];

/// Select catch rows for a session and order them newest first. Equal UTC
/// timestamps retain their original row order. A null selection reads all rows.
///
/// # Safety
/// The caller holds valid, disjoint buffers for `count` timestamps, `count` GUIDs
/// and `capacity` output indices. A non-null selection points to one GUID.
#[no_mangle]
pub unsafe extern "C" fn journal_order_rows(
    utc_ticks: *const i64,
    session_keys: *const u8,
    count: i32,
    selected_session: *const u8,
    output: *mut i32,
    capacity: i32,
) -> i32 {
    if !(0..=20_000).contains(&count) || capacity < count {
        return -1;
    }
    if count == 0 {
        return 0;
    }
    if utc_ticks.is_null() || session_keys.is_null() || output.is_null() {
        return -1;
    }

    let ticks = unsafe { std::slice::from_raw_parts(utc_ticks, count as usize) };
    let sessions = unsafe { std::slice::from_raw_parts(session_keys.cast::<SessionId>(), count as usize) };
    let selected = unsafe { selected_session.cast::<SessionId>().as_ref() };
    let indices = unsafe { std::slice::from_raw_parts_mut(output, count as usize) };

    let mut written = 0;
    for (index, session) in sessions.iter().enumerate() {
        if selected.is_none() || selected == Some(session) {
            indices[written] = index as i32;
            written += 1;
        }
    }
    indices[..written].sort_unstable_by(|&a, &b| {
        ticks[b as usize].cmp(&ticks[a as usize]).then_with(|| a.cmp(&b))
    });
    written as i32
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn filters_sessions_and_retains_input_order_for_tied_utc_ticks() {
        let ticks = [i64::MIN, 8, i64::MAX, 8, 8, -4];
        let a = [1; 16];
        let b = [2; 16];
        let sessions = [a, b, b, a, a, a];
        let mut indices = [-9i32; 6];
        let mut run = |selection| unsafe {
            journal_order_rows(
                ticks.as_ptr(),
                sessions.as_ptr().cast(),
                ticks.len() as i32,
                selection,
                indices.as_mut_ptr(),
                indices.len() as i32,
            );
            indices
        };
        assert_eq!(run(a.as_ptr()), [3, 4, 5, 0, -9, -9]);
        assert_eq!(run(std::ptr::null()), [2, 1, 3, 4, 5, 0]);
    }

    #[test]
    fn handles_empty_results_and_rejects_bad_buffers() {
        let ticks = [42i64];
        let sessions = [[1u8; 16]];
        let mut indices = [-9i32];
        let null = std::ptr::null();
        let selection = [2u8; 16];
        unsafe {
            assert_eq!(journal_order_rows(ticks.as_ptr(), sessions.as_ptr().cast(), 1,
                selection.as_ptr(), indices.as_mut_ptr(), 1), 0);
            assert_eq!(journal_order_rows(ticks.as_ptr(), sessions.as_ptr().cast(), 1,
                null, indices.as_mut_ptr(), 0), -1);
            assert_eq!(journal_order_rows(ticks.as_ptr(), null, 1, null, indices.as_mut_ptr(), 1), -1);
            assert_eq!(journal_order_rows(std::ptr::null(), null, 0, null, std::ptr::null_mut(), 0), 0);
        }
        assert_eq!(indices, [-9]);
    }
}
