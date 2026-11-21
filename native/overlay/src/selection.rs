const TIED_KEYS: i32 = -2;

/// Filter and rank camera candidates using the managed distance, alignment and priority.
///
/// # Safety
/// The caller provides disjoint buffers: `count` triples of `f32` and room for
/// `capacity` indices. Priority is a managed rank encoded as 0, 1 or 2.
#[no_mangle]
pub unsafe extern "C" fn overlay_select(
    input: *const f32,
    count: i32,
    maximum_squared: f32,
    minimum_alignment: f32,
    output: *mut i32,
    capacity: i32,
) -> i32 {
    if count < 0 || capacity < count || input.is_null() || output.is_null() {
        return -1;
    }
    let count = count as usize;
    if count > isize::MAX as usize / std::mem::size_of::<[f32; 3]>() {
        return -1;
    }
    let input = unsafe { std::slice::from_raw_parts(input.cast::<[f32; 3]>(), count) };
    let output = unsafe { std::slice::from_raw_parts_mut(output, count) };
    let mut written = 0;
    for (index, &[distance, alignment, _]) in input.iter().enumerate() {
        if distance.is_finite()
            && distance > 1e-6
            && distance <= maximum_squared
            && alignment.is_finite()
            && (minimum_alignment <= -1.0 || alignment >= minimum_alignment)
        {
            output[written] = index as i32;
            written += 1;
        }
    }

    // The bit order of finite positive squared distances is their numeric order.
    let key = |index: i32| {
        let [distance, _, priority] = input[index as usize];
        (priority as i32, distance.to_bits())
    };
    let selected = &mut output[..written];
    selected.sort_unstable_by_key(|&index| key(index));
    if selected
        .windows(2)
        .any(|pair| key(pair[0]) == key(pair[1]))
    {
        return TIED_KEYS;
    }
    written as i32
}
