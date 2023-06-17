void AddPointCloud(std::string name, point_cloud what)
{
	auto it = pointClouds.find(name);
	if (it != pointClouds.end())
		pointClouds.erase(it);

	gpu_point_cloud gbuf;
	gbuf.pc = what;
	sg_buffer_desc vbufDesc = {
		.data = { what.x_y_z_Sz.data(), what.x_y_z_Sz.size() * sizeof(glm::vec4)},
	};
	gbuf.pcBuf = sg_make_buffer(&vbufDesc);
	sg_buffer_desc cbufDesc = {
		.data = { what.color.data(), what.color.size() * sizeof(glm::vec4) },
	};
	gbuf.colorBuf = sg_make_buffer(&cbufDesc);
	pointClouds[name] = gbuf;

	std::cout << "Added point cloud '" << name << "'" << std::endl;
}

void RemovePointCloud(std::string name) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		pointClouds.erase(it);
		std::cout << "Removed point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}

void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		auto& gpu = it->second;
		gpu.pc.position = new_position;
		gpu.pc.quaternion = new_quaternion;
		std::cout << "Modified point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}