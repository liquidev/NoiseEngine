use std::sync::Arc;

use ash::{vk, extensions::khr};

use crate::rendering::presentation::window::Window;

use super::instance::VulkanInstance;

pub struct VulkanSurface {
    instance: Arc<VulkanInstance>,
    window: Arc<dyn Window>,
    inner: vk::SurfaceKHR
}

impl VulkanSurface {
    pub fn new(instance: Arc<VulkanInstance>, window: Arc<dyn Window>, inner: vk::SurfaceKHR) -> Self {
        Self { instance, window, inner }
    }

    pub fn inner(&self) -> vk::SurfaceKHR {
        self.inner
    }

    pub fn instance(&self) -> &Arc<VulkanInstance> {
        &self.instance
    }

    pub fn window(&self) -> &Arc<dyn Window> {
        &self.window
    }
}

impl Drop for VulkanSurface {
    fn drop(&mut self) {
        let surface = khr::Surface::new(self.instance.library(), self.instance.inner());
        unsafe {
            surface.destroy_surface(self.inner, None);
        }
    }
}