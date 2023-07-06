use std::{marker::PhantomData, slice};

#[repr(C)]
pub struct InteropReadOnlySpan<'a, T> {
    reference: *const T,
    length: i32,
    phantom_data: PhantomData<&'a T>,
}

impl<'a, T> InteropReadOnlySpan<'a, T> {
    pub fn len(&self) -> usize {
        self.length as usize
    }

    pub fn is_empty(&self) -> bool {
        self.length == 0
    }

    pub fn as_ptr(&self) -> *const T {
        self.reference
    }
}

impl<'a, T> From<InteropReadOnlySpan<'a, T>> for &'a [T] {
    fn from(span: InteropReadOnlySpan<'a, T>) -> Self {
        unsafe { slice::from_raw_parts(span.reference, span.length as usize) }
    }
}

impl<'a, T> From<&'a [T]> for InteropReadOnlySpan<'a, T> {
    fn from(slice: &'a [T]) -> Self {
        InteropReadOnlySpan {
            reference: slice.as_ptr(),
            length: slice.len() as i32,
            phantom_data: PhantomData,
        }
    }
}
