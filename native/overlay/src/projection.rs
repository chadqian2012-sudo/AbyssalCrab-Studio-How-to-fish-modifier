const CAMERA_LENGTH: usize = 37;
const INPUT_STRIDE: usize = 7;
const RESULT_STRIDE: usize = 5;

struct Projection<'a> {
    view: &'a [f32; 16],
    projection: &'a [f32; 16],
    viewport: [f32; 4],
    near: f32,
}

#[derive(Clone, Copy)]
struct Point {
    world: [f32; 3],
    depth: f32,
    screen: Option<[f32; 2]>,
}

fn dot(m: &[f32; 16], row: usize, [x, y, z, w]: [f32; 4]) -> f32 {
    let offset = row * 4;
    m[offset] * x + m[offset + 1] * y + m[offset + 2] * z + m[offset + 3] * w
}

impl<'a> Projection<'a> {
    fn new(camera: &'a [f32]) -> Option<Self> {
        if camera.iter().any(|value| !value.is_finite()) {
            return None;
        }
        let [x, y, width, height, near] = camera[32..].try_into().ok()?;
        if width <= 0.0 || height <= 0.0 || near <= 0.0 || !(x + width).is_finite() || !(y + height).is_finite() {
            return None;
        }
        Some(Self {
            view: camera[..16].try_into().ok()?,
            projection: camera[16..32].try_into().ok()?,
            viewport: [x, y, width, height],
            near,
        })
    }

    fn point(&self, world: [f32; 3]) -> Point {
        let homogeneous = [world[0], world[1], world[2], 1.0];
        let eye = std::array::from_fn(|row| dot(self.view, row, homogeneous));
        let w = dot(self.projection, 3, eye);
        let [x, y, width, height] = self.viewport;
        let px = x + (dot(self.projection, 0, eye) / w + 1.0) * 0.5 * width;
        let py = y + (dot(self.projection, 1, eye) / w + 1.0) * 0.5 * height;
        Point {
            world,
            // Unity's camera-space forward axis is -Z. Clip-space W varies by projection.
            depth: -eye[2],
            screen: (w.is_finite() && px.is_finite() && py.is_finite()).then_some([px, py]),
        }
    }

    fn near_point(&self, a: Point, b: Point) -> Option<[f32; 2]> {
        let amount = ((self.near as f64 - a.depth as f64) / (b.depth as f64 - a.depth as f64)) as f32;
        let world = std::array::from_fn(|i| a.world[i] + (b.world[i] - a.world[i]) * amount);
        self.point(world).screen
    }

    fn segment(&self, a: Point, b: Point) -> Option<[f32; 4]> {
        if !a.depth.is_finite() || !b.depth.is_finite() || a.depth < self.near && b.depth < self.near {
            return None;
        }
        let [x0, y0] = if a.depth < self.near { self.near_point(a, b) } else { a.screen }?;
        let [x1, y1] = if b.depth < self.near { self.near_point(a, b) } else { b.screen }?;
        Some([x0, y0, x1, y1])
    }

    fn clip_rect(&self, [left, bottom, right, top]: [f32; 4]) -> Option<[f32; 4]> {
        let [x, y, width, height] = self.viewport;
        let left = left.max(x);
        let right = right.min(x + width);
        let bottom = bottom.max(y);
        let top = top.min(y + height);
        (right > left && top > bottom).then_some([left, bottom, right, top])
    }

    fn marker(&self, world: [f32; 3]) -> Option<[f32; 4]> {
        let point = self.point(world);
        if !point.depth.is_finite() || point.depth < self.near {
            return None;
        }
        let [x, y] = point.screen?;
        self.clip_rect([x - 4.0, y - 6.0, x + 4.0, y + 6.0])
    }

    fn bounds(&self, center: [f32; 3], extents: [f32; 3]) -> Option<[f32; 4]> {
        if extents.iter().any(|&extent| extent < 0.0) {
            return None;
        }
        let corners: [Point; 8] = std::array::from_fn(|i| {
            self.point(std::array::from_fn(|axis| {
                let sign = if i & (1 << axis) == 0 { -1.0 } else { 1.0 };
                center[axis] + extents[axis] * sign
            }))
        });

        let [mut left, mut bottom, mut right, mut top] = [f32::INFINITY, f32::INFINITY, f32::NEG_INFINITY, f32::NEG_INFINITY];
        for i in 0..corners.len() {
            for bit in [1, 2, 4] {
                if i & bit != 0 {
                    continue;
                }
                if let Some([x0, y0, x1, y1]) = self.segment(corners[i], corners[i | bit]) {
                    left = left.min(x0.min(x1));
                    right = right.max(x0.max(x1));
                    bottom = bottom.min(y0.min(y1));
                    top = top.max(y0.max(y1));
                }
            }
        }
        self.clip_rect([left, bottom, right, top])
    }

    fn project(&self, row: &[f32]) -> Option<[f32; 4]> {
        if row[1..].iter().any(|value| !value.is_finite()) {
            return None;
        }
        let a = [row[1], row[2], row[3]];
        let b = [row[4], row[5], row[6]];
        match row[0] {
            0.0 => self.marker(a),
            1.0 => self.bounds(a, b),
            2.0 => self.segment(self.point(a), self.point(b)),
            _ => None,
        }
    }
}

/// Camera is two row-major matrices (world-to-view and CPU projection), then viewport
/// x/y/width/height and near. Each input row is [kind, ax, ay, az, bx, by, bz], with
/// 0 = marker, 1 = bounds (center/extents), and 2 = segment (endpoints).
/// An output row is [visible, x0, y0, x1, y1]. Invisible rows are zeroed in place.
///
/// # Safety
/// The caller must supply aligned, initialized camera and input buffers of 37 and
/// count * 7 floats and a disjoint, writable output buffer of capacity * 5 floats.
#[no_mangle]
pub unsafe extern "C" fn overlay_project(
    camera: *const f32,
    input: *const f32,
    count: i32,
    output: *mut f32,
    capacity: i32,
) -> i32 {
    if count < 0 || capacity < count || camera.is_null() || input.is_null() || output.is_null() {
        return -1;
    }
    let Some(input_len) = (count as usize).checked_mul(INPUT_STRIDE) else {
        return -1;
    };
    let Some(output_len) = (count as usize).checked_mul(RESULT_STRIDE) else {
        return -1;
    };
    let max_len = isize::MAX as usize / std::mem::size_of::<f32>();
    if input_len > max_len || output_len > max_len {
        return -1;
    }
    let camera = std::slice::from_raw_parts(camera, CAMERA_LENGTH);
    let input = std::slice::from_raw_parts(input, input_len);
    let output = std::slice::from_raw_parts_mut(output, output_len);
    let Some(camera) = Projection::new(camera) else {
        output.fill(0.0);
        return count;
    };

    for (row, result) in input.chunks_exact(INPUT_STRIDE).zip(output.chunks_exact_mut(RESULT_STRIDE)) {
        if let Some([x0, y0, x1, y1]) = camera.project(row) {
            result.copy_from_slice(&[1.0, x0, y0, x1, y1]);
        } else {
            result.fill(0.0);
        }
    }
    count
}

#[cfg(test)]
mod tests {
    use super::*;

    #[rustfmt::skip]
    fn perspective() -> [f32; CAMERA_LENGTH] {
        [
            1.0, 0.0,  0.0, 0.0,
            0.0, 1.0,  0.0, 0.0,
            0.0, 0.0, -1.0, 0.0,
            0.0, 0.0,  0.0, 1.0,
            1.0, 0.0,  0.0, 0.0,
            0.0, 1.0,  0.0, 0.0,
            0.0, 0.0, -1.0, -2.0,
            0.0, 0.0, -1.0, 0.0,
            100.0, 50.0, 400.0, 200.0, 1.0,
        ]
    }

    fn run(camera: &[f32; CAMERA_LENGTH], rows: &[[f32; INPUT_STRIDE]]) -> Vec<f32> {
        let count = rows.len() as i32;
        let mut output = vec![-9.0; rows.len() * RESULT_STRIDE];
        let result = unsafe {
            overlay_project(camera.as_ptr(), rows.as_ptr().cast(), count, output.as_mut_ptr(), count)
        };
        assert_eq!(result, count);
        output
    }

    fn close(actual: &[f32], expected: &[f32]) {
        assert_eq!(actual.len(), expected.len());
        for (a, b) in actual.iter().zip(expected) {
            assert!((a - b).abs() <= 0.0001, "{a} != {b}");
        }
    }

    #[test]
    fn offset_viewport_and_asymmetric_perspective() {
        let mut camera = perspective();
        camera[3] = -10.0;
        camera[7] = 2.0;
        camera[11] = 5.0;
        camera[16] = 2.0;
        camera[18] = 0.25;
        camera[21] = 3.0;
        camera[22] = -0.5;
        let output = run(&camera, &[[0.0, 11.0, -2.0, 9.0, 0.0, 0.0, 0.0]]);
        close(&output, &[1.0, 346.0, 194.0, 354.0, 206.0]);
    }

    #[test]
    fn orthographic_depth_comes_from_rotated_view() {
        let mut camera = perspective();
        camera[0] = 0.0;
        camera[2] = 1.0;
        camera[3] = -5.0;
        camera[7] = 2.0;
        camera[8] = -1.0;
        camera[10] = 0.0;
        camera[11] = 10.0;
        camera[16] = 0.5;
        camera[19] = 0.25;
        camera[21] = 0.5;
        camera[23] = -0.25;
        camera[30] = 0.0;
        camera[31] = 1.0;
        let output = run(&camera, &[
            [0.0, 11.0, -2.0, 5.0, 0.0, 0.0, 0.0],
            [0.0, 10.5, -2.0, 5.0, 0.0, 0.0, 0.0],
            [0.0, 10000.0, -2.0, 5.0, 0.0, 0.0, 0.0],
        ]);
        let visible = [1.0, 346.0, 119.0, 354.0, 131.0];
        close(&output[..5], &visible);
        assert_eq!(output[5..10], [0.0; 5]);
        close(&output[10..], &visible);
    }

    #[test]
    fn box_and_segment_reproject_the_near_intersection() {
        let output = run(&perspective(), &[
            [2.0, -0.5, 0.0, -1.0, 1.0, 0.0, 3.0],
            [2.0, 1.0, 0.0, 3.0, -0.5, 0.0, -1.0],
            [1.0, 0.0, 0.0, 1.5, 2.0, 0.5, 1.0],
            [1.0, 0.0, 0.0, -2.0, 2.0, 0.5, 1.0],
        ]);
        close(&output[..5], &[1.0, 350.0, 150.0, 366.6667, 150.0]);
        close(&output[5..10], &[1.0, 366.6667, 150.0, 350.0, 150.0]);
        close(&output[10..15], &[1.0, 100.0, 100.0, 500.0, 200.0]);
        assert_eq!(output[15..], [0.0; 5]);
    }

    #[test]
    fn visibility_respects_edges_and_rejects_invalid_geometry() {
        let output = run(&perspective(), &[
            [0.0, 1.01, 0.0, 1.0, 0.0, 0.0, 0.0],
            [0.0, 1.1, 0.0, 1.0, 0.0, 0.0, 0.0],
            [1.0, 10.0, 0.0, 2.0, 0.5, 0.5, 0.5],
            [2.0, f32::NAN, 0.0, 1.0, 1.0, 0.0, 1.0],
            [3.0, 0.0, 0.0, 2.0, 1.0, 0.0, 2.0],
        ]);
        close(&output[..5], &[1.0, 498.0, 144.0, 500.0, 156.0]);
        assert_eq!(output[5..], [0.0; 20]);
    }

    #[test]
    fn call_validates_capacity_and_zeroes_an_invalid_camera_frame() {
        let mut camera = perspective();
        let input = [0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0];
        let mut output = [-9.0; RESULT_STRIDE];
        let result = unsafe {
            overlay_project(camera.as_ptr(), input.as_ptr(), 1, output.as_mut_ptr(), 0)
        };
        assert_eq!(result, -1);
        assert_eq!(output, [-9.0; RESULT_STRIDE]);
        camera[36] = f32::NAN;
        let result = unsafe {
            overlay_project(camera.as_ptr(), input.as_ptr(), 1, output.as_mut_ptr(), 1)
        };
        assert_eq!(result, 1);
        assert_eq!(output, [0.0; RESULT_STRIDE]);
    }
}
