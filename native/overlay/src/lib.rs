fn boundary(p: f64, q: f64, start: &mut f64, end: &mut f64) -> bool {
    if p == 0.0 { return q >= 0.0; }
    let t = q / p;
    if p < 0.0 { if t > *end { return false; } if t > *start { *start = t; } }
    else { if t < *start { return false; } if t < *end { *end = t; } }
    true
}

#[no_mangle]
pub extern "C" fn overlay_abi() -> i32 { 1 }

// The caller pins disjoint input/output buffers for the duration of this call.
#[no_mangle]
pub unsafe extern "C" fn overlay_build(input: *const f32, count: i32, output: *mut f32, capacity: i32) -> i32 {
    if count < 0 || capacity < count || input.is_null() || output.is_null() { return -1; }
    let Some(input_len) = (count as usize).checked_mul(13) else { return -1; };
    let Some(output_len) = (capacity as usize).checked_mul(12) else { return -1; };
    if input_len > isize::MAX as usize / 4 || output_len > isize::MAX as usize / 4 { return -1; }
    let input = std::slice::from_raw_parts(input, input_len);
    let output = std::slice::from_raw_parts_mut(output, output_len);
    let mut written = 0;
    for s in input.chunks_exact(13) {
        if s[..8].iter().any(|v| !v.is_finite()) || !s[12].is_finite() || s[12] <= 0.0 || s[6] <= s[4] || s[7] <= s[5] { continue; }
        let (x, y, dx, dy) = (s[0] as f64, s[1] as f64, s[2] as f64-s[0] as f64, s[3] as f64-s[1] as f64);
        let (mut start, mut end) = (0.0, 1.0);
        if !boundary(-dx,x-s[4] as f64,&mut start,&mut end) || !boundary(dx,s[6] as f64-x,&mut start,&mut end) ||
            !boundary(-dy,y-s[5] as f64,&mut start,&mut end) || !boundary(dy,s[7] as f64-y,&mut start,&mut end) { continue; }
        let (x0,y0,x1,y1) = ((x+start*dx) as f32,(y+start*dy) as f32,(x+end*dx) as f32,(y+end*dy) as f32);
        let (dx,dy) = (x1 as f64-x0 as f64,y1 as f64-y0 as f64);
        let length = (dx*dx+dy*dy).sqrt();
        if length < 0.1 { continue; }
        let (nx,ny) = ((-dy/length*s[12] as f64*0.5) as f32,(dx/length*s[12] as f64*0.5) as f32);
        let d=written*12;
        output[d..d+12].copy_from_slice(&[x0+nx,y0+ny,x1+nx,y1+ny,x1-nx,y1-ny,x0-nx,y0-ny,s[8],s[9],s[10],s[11]]);
        written+=1;
    }
    written as i32
}
