void Camera::init(glm::vec3 stare, float dist, float width, float height, float minDist)
{
    this->stare = stare;
    this->distance = dist;
    this->_width = width;
    this->_height = height;
    this->_minDist = minDist;
    Reset(stare, dist);
    Resize(width, height);
}

void Camera::RotateAzimuth(float delta)
{
	Azimuth += delta * rotateSpeed;
	Azimuth = fmod(Azimuth, 2 * M_PI);
	// Apply azimuth range constraints
	Azimuth = glm::clamp(Azimuth, azimuth_range.x, azimuth_range.y);
	UpdatePosition();
}

void Camera::RotateAltitude(float delta)
{
	Altitude += delta * rotateSpeed;
	// Apply altitude range constraints
	Altitude = glm::clamp(Altitude, altitude_range.x, altitude_range.y);
	UpdatePosition();
}

void Camera::Rotate(float deltaAlt, float deltaAzi)
{
	Azimuth += deltaAzi * rotateSpeed;
	Azimuth = fmod(Azimuth, 2 * M_PI);
	// Apply azimuth range constraints
	Azimuth = glm::clamp(Azimuth, azimuth_range.x, azimuth_range.y);
	
	Altitude += deltaAlt * rotateSpeed;
	// Apply altitude range constraints
	Altitude = glm::clamp(Altitude, altitude_range.x, altitude_range.y);
	
	stare = position - glm::vec3(
		distance * cos(Altitude) * cos(Azimuth),
		distance * cos(Altitude) * sin(Azimuth),
		distance * sin(Altitude)
	);

	// Apply pan_range constraint to stare position
	if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX || 
		pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX || 
		pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) {
		if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX) 
			stare.x = glm::clamp(stare.x, pan_range_x.x, pan_range_x.y);
		if (pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX) 
			stare.y = glm::clamp(stare.y, pan_range_y.x, pan_range_y.y);
		if (pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) 
			stare.z = glm::clamp(stare.z, pan_range_z.x, pan_range_z.y);
	}

	UpdatePosition();
}

void Camera::GoFrontBack(float delta)
{
	// Calculate the direction vector from position to stare
	glm::vec3 direction = glm::normalize(stare - position);
	
	// Move both stare and position along this direction
	glm::vec3 newStare = stare + direction * delta;
	glm::vec3 newPosition = position + direction * delta;
	
	stare = newStare;
	position = newPosition;
}

void Camera::PanLeftRight(float delta)
{
	glm::vec3 newStare = stare + moveRight * delta;
	glm::vec3 newPosition = position + moveRight * delta;
	
	// Apply pan_range constraint to stare position
	if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX || 
		pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX || 
		pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) {
		if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX) 
			newStare.x = glm::clamp(newStare.x, pan_range_x.x, pan_range_x.y);
		if (pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX) 
			newStare.y = glm::clamp(newStare.y, pan_range_y.x, pan_range_y.y);
		if (pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) 
			newStare.z = glm::clamp(newStare.z, pan_range_z.x, pan_range_z.y);
		// Adjust position to maintain camera distance relationship
		newPosition = newStare + (position - stare);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::PanBackForth(float delta)
{
	glm::vec3 newStare = stare + moveFront * delta;
	glm::vec3 newPosition = position + moveFront * delta;
	
	// Apply pan_range constraint to stare position
	if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX || 
		pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX || 
		pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) {
		if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX) 
			newStare.x = glm::clamp(newStare.x, pan_range_x.x, pan_range_x.y);
		if (pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX) 
			newStare.y = glm::clamp(newStare.y, pan_range_y.x, pan_range_y.y);
		if (pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) 
			newStare.z = glm::clamp(newStare.z, pan_range_z.x, pan_range_z.y);
		// Adjust position to maintain camera distance relationship
		newPosition = newStare + (position - stare);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::ElevateUpDown(float delta)
{
	glm::vec3 newStare = stare + glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	glm::vec3 newPosition = position + glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	
	// Apply pan_range constraint to stare position
	if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX || 
		pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX || 
		pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) {
		if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX) 
			newStare.x = glm::clamp(newStare.x, pan_range_x.x, pan_range_x.y);
		if (pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX) 
			newStare.y = glm::clamp(newStare.y, pan_range_y.x, pan_range_y.y);
		if (pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) 
			newStare.z = glm::clamp(newStare.z, pan_range_z.x, pan_range_z.y);
		// Adjust position to maintain camera distance relationship
		newPosition = newStare + (position - stare);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::Zoom(float delta)
{
	distance = glm::clamp(distance * (1 + delta), _minDist, 500.0f);
	UpdatePosition();
}

void Camera::Reset(glm::vec3 stare, float dist)
{
	this->stare = glm::vec3(stare.x, stare.y, 0.0f);
	distance = dist;
	Azimuth = -M_PI_2;
	Altitude = M_PI_2;
	UpdatePosition();
}

void Camera::Resize(float width, float height)
{
	_width = width;
	_height = height;
	_aspectRatio = _width / _height;
}


bool Camera::test_apply_external()
{
	return camera_object->anchor.obj != nullptr || glm::distance(camera_object->current_pos, camera_object->target_position) > 0.01f;
}

glm::vec3 Camera::getPos()
{
    if (!test_apply_external()) return position;
    // anchor_type: 0 both, 1 stare only, 2 position only, 3 full copy.
    if (anchor_type == 2 || anchor_type == 0)
        return position + camera_object->current_pos;
    return position;
}
glm::vec3 Camera::getStare()
{
    if (!test_apply_external()) return stare;
    if (anchor_type == 1 || anchor_type == 0)
        return stare + camera_object->current_pos;
    return stare;
}

glm::mat4 Camera::GetViewMatrix()
{
	auto st = stare;
	auto pos = position;
    if (test_apply_external()) {
		if (anchor_type == 3) {
			for (int i=0; i<MAX_VIEWPORTS; ++i)
				if (i!=working_viewport_id && ui.viewports[i].camera_obj==camera_object->anchor.obj)
					return ui.viewports[i].camera.vm;
		}
        if (anchor_type == 0 || anchor_type == 1) st += camera_object->current_pos;
        if (anchor_type == 0 || anchor_type == 2) pos += camera_object->current_pos;
    }
	auto mat = glm::lookAt(pos, st, up);
	if (isnan(mat[0][0])) {
#if DEBUG
		throw "WTF? camera mat[0][0] is nan.";
#endif
		Reset({ 0,0,0 }, 10);
	}
	return vm = mat;
	// todo: try better view point
	// if (abs(position.z-stare.z)>distance/2)
	// return glm::lookAt(position, stare, up);
	// else
	// 	return lookAt(position, glm::vec3(stare.x,stare.y,))
}

glm::mat4 Camera::GetProjectionMatrix()
{
	if (ProjectionMode == 0) {
		return glm::perspective(glm::radians(_fov), _aspectRatio, cam_near, cam_far);
	}
	return glm::ortho(-_width * distance / OrthoFactor, _width * distance / OrthoFactor, -_height * distance / OrthoFactor, _height * distance / OrthoFactor, cam_near, cam_far);
}

void Camera::UpdatePosition()
{
	// todo:...
	// if (test_apply_external() && anchor_type != 0)
	// {
	// 	// set up, moveFront, moveRight according to 
	// 	return;
	// }
	if (abs(Altitude - M_PI_2) < gap || abs(Altitude + M_PI_2)<gap) {
		position = stare + glm::vec3(0.0f, 0.0f, (Altitude > 0 ? distance : -distance));
		glm::vec3 n = glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		moveRight = glm::normalize(glm::cross(glm::vec3(0.0f, 0.0f, 1.0f), n));
		up = moveFront = Altitude > 0 ? -n : n;
	}
	else {
		position = stare + glm::vec3(
			distance * cos(Altitude) * cos(Azimuth),
			distance * cos(Altitude) * sin(Azimuth),
			distance * sin(Altitude)
		);
		up = glm::vec3(0.0f, 0.0f, 1.0f);
		moveFront = -glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		if (Altitude < 0) moveFront *= -1;
		moveRight = glm::normalize(glm::cross(up, position - stare));
	}

	// Apply xyz_range constraints to position if provided
	bool position_constrained = false;
	if (x_range.x != -FLT_MAX || x_range.y != FLT_MAX) {
		glm::vec3 old_position = position;
		position.x = glm::clamp(position.x, x_range.x, x_range.y);
		position.y = glm::clamp(position.y, y_range.x, y_range.y);
		position.z = glm::clamp(position.z, z_range.x, z_range.y);
		if (position != old_position) {
			position_constrained = true;
			// Recalculate stare from constrained position
			stare = position - glm::vec3(
				distance * cos(Altitude) * cos(Azimuth),
				distance * cos(Altitude) * sin(Azimuth),
				distance * sin(Altitude)
			);
		}
	}
	
	// Apply pan_range constraint to stare position
	bool stare_constrained = false;
	if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX || 
		pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX || 
		pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) {
		glm::vec3 old_stare = stare;
		if (pan_range_x.x != -FLT_MAX || pan_range_x.y != FLT_MAX) 
			stare.x = glm::clamp(stare.x, pan_range_x.x, pan_range_x.y);
		if (pan_range_y.x != -FLT_MAX || pan_range_y.y != FLT_MAX) 
			stare.y = glm::clamp(stare.y, pan_range_y.x, pan_range_y.y);
		if (pan_range_z.x != -FLT_MAX || pan_range_z.y != FLT_MAX) 
			stare.z = glm::clamp(stare.z, pan_range_z.x, pan_range_z.y);
		if (stare != old_stare) {
			stare_constrained = true;
		}
	}
	
	// If both position and stare were constrained, we need to recompute distance or adjust one
	if (position_constrained && stare_constrained) {
		// Prioritize stare constraint - recalculate position from constrained stare
		position = stare + glm::vec3(
			distance * cos(Altitude) * cos(Azimuth),
			distance * cos(Altitude) * sin(Azimuth),
			distance * sin(Altitude)
		);
		// If the recalculated position violates xyz_range again, adjust distance instead
		glm::vec3 unconstrained_position = position;
		position.x = glm::clamp(position.x, x_range.x, x_range.y);
		position.y = glm::clamp(position.y, y_range.x, y_range.y);
		position.z = glm::clamp(position.z, z_range.x, z_range.y);
		if (position != unconstrained_position) {
			// Recalculate distance to fit both constraints
			distance = glm::length(position - stare);
		}
	} else if (stare_constrained && !position_constrained) {
		// Only stare was constrained - recalculate position
		position = stare + glm::vec3(
			distance * cos(Altitude) * cos(Azimuth),
			distance * cos(Altitude) * sin(Azimuth),
			distance * sin(Altitude)
		);
	}
}
 