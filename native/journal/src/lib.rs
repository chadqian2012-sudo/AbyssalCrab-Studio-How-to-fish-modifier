mod query;
mod validation;

#[no_mangle]
pub extern "C" fn journal_abi() -> i32 {
    1
}
